﻿//-----------------------------------------------------------------------
// <copyright file="SwaggerMiddleware.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/NSwag/NSwag/blob/master/LICENSE.md</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NSwag.AspNetCore.Middlewares
{
    /// <summary>Generates a Swagger specification on a given path.</summary>
    public class SwaggerMiddleware
    {
        private readonly RequestDelegate _nextDelegate;
        private readonly string _documentName;
        private readonly IApiDescriptionGroupCollectionProvider _apiDescriptionGroupCollectionProvider;
        private readonly SwaggerDocumentProvider _documentProvider;
        private readonly SwaggerMiddlewareSettings _settings;

        private int _version;
        private string _swaggerJson;
        private Exception _swaggerException;
        private DateTimeOffset _schemaTimestamp;

        /// <summary>Initializes a new instance of the <see cref="WebApiToSwaggerMiddleware"/> class.</summary>
        /// <param name="nextDelegate">The next delegate.</param>
        public SwaggerMiddleware(
            RequestDelegate nextDelegate,
            IServiceProvider serviceProvider,
            string documentName,
            Action<SwaggerMiddlewareSettings> configure)
        {
            _nextDelegate = nextDelegate;
            _documentName = documentName;

            _apiDescriptionGroupCollectionProvider = serviceProvider.GetService<IApiDescriptionGroupCollectionProvider>() ??
                throw new InvalidOperationException("API Explorer not registered in DI.");
            _documentProvider = serviceProvider.GetService<SwaggerDocumentProvider>() ??
                throw new InvalidOperationException("The NSwag DI services are not registered: Call " + nameof(NSwagServiceCollectionExtensions.AddSwagger) + "() in ConfigureServices().");

            var settings = new SwaggerMiddlewareSettings();
            configure?.Invoke(settings);
            _settings = settings;
        }

        /// <summary>Invokes the specified context.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The task.</returns>
        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path.HasValue && string.Equals(context.Request.Path.Value.Trim('/'), _settings.Path.Trim('/'), StringComparison.OrdinalIgnoreCase))
            {
                var schemaJson = await GenerateSwaggerAsync(context);
                context.Response.StatusCode = 200;
                context.Response.Headers["Content-Type"] = "application/json; charset=utf-8";
                await context.Response.WriteAsync(schemaJson);
            }
            else
            {
                await _nextDelegate(context);
            }
        }

        /// <summary>Generates the Swagger specification.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The Swagger specification.</returns>
        protected virtual async Task<string> GenerateSwaggerAsync(HttpContext context)
        {
            if (_swaggerException != null && _schemaTimestamp + _settings.ExceptionCacheTime > DateTimeOffset.UtcNow)
            {
                throw _swaggerException;
            }

            var apiDescriptionGroups = _apiDescriptionGroupCollectionProvider.ApiDescriptionGroups;
            if (apiDescriptionGroups.Version == Volatile.Read(ref _version) && _swaggerJson != null)
            {
                return _swaggerJson;
            }

            try
            {
                var document = await _documentProvider.GenerateAsync(_documentName);

                document.Host = context.Request.Host.Value ?? "";
                document.Schemes.Add(context.Request.Scheme == "http" ? SwaggerSchema.Http : SwaggerSchema.Https);
                document.BasePath = context.Request.PathBase.Value?.Substring(0, context.Request.PathBase.Value.Length - (_settings.MiddlewareBasePath?.Length ?? 0)) ?? "";

                _settings.PostProcess?.Invoke(context.Request, document);

                _swaggerJson = document.ToJson();
                _swaggerException = null;
                _version = apiDescriptionGroups.Version;
                _schemaTimestamp = DateTimeOffset.UtcNow;
            }
            catch (Exception exception)
            {
                _swaggerJson = null;
                _swaggerException = exception;
                _schemaTimestamp = DateTimeOffset.UtcNow;
                throw _swaggerException;
            }

            return _swaggerJson;
        }
    }
}