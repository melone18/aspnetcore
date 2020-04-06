// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web;
using BasicTestApp.AuthTest;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BasicTestApp
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await SimulateErrorsIfNeededForTest();

            var builder = WebAssemblyHostBuilder.CreateDefault(args);

            var httpClient = new HttpClient();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("WEBASSEMBLY")))
            {
                // Needed because the test server runs on a different port than the client app,
                // and we want to test sending/receiving cookies under this config
                httpClient = new HttpClient(new ConfigureForCorsHandler(new WebAssemblyHttpHandler()));
            }
            httpClient.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);

            builder.Services.AddSingleton(httpClient);

            builder.RootComponents.Add<Index>("root");
            builder.Services.AddSingleton<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
            builder.Services.AddAuthorizationCore(options =>
            {
                options.AddPolicy("NameMustStartWithB", policy =>
                    policy.RequireAssertion(ctx => ctx.User.Identity.Name?.StartsWith("B") ?? false));
            });

            builder.Logging.Services.AddSingleton<ILoggerProvider, PrependMessageLoggerProvider>(s => new PrependMessageLoggerProvider("Custom logger", s.GetService<IJSRuntime>()));
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            var host = builder.Build();
            ConfigureCulture(host);

            await host.RunAsync();
        }

        private static void ConfigureCulture(WebAssemblyHost host)
        {
            // In the absence of a specified value, we want the culture to be en-US so that the tests for bind can work consistently.
            var culture = new CultureInfo("en-US");

            Uri uri = null;
            try
            {
                uri = new Uri(host.Services.GetService<NavigationManager>().Uri);
            }
            catch (ArgumentException)
            {
                // Some of our tests set this application up incorrectly so that querying NavigationManager.Uri throws.
            }

            if (uri != null && HttpUtility.ParseQueryString(uri.Query)["culture"] is string cultureName)
            {
                culture = new CultureInfo(cultureName);
            }

            // CultureInfo.CurrentCulture is async-scoped and will not affect the culture in sibling scopes.
            // Use CultureInfo.DefaultThreadCurrentCulture instead to modify the application's default scope.
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        // Supports E2E tests in StartupErrorNotificationTest
        private static async Task SimulateErrorsIfNeededForTest()
        {
            var currentUrl = DefaultWebAssemblyJSRuntime.Instance.Invoke<string>("getCurrentUrl");
            if (currentUrl.Contains("error=sync"))
            {
                throw new InvalidTimeZoneException("This is a synchronous startup exception");
            }

            await Task.Yield();

            if (currentUrl.Contains("error=async"))
            {
                throw new InvalidTimeZoneException("This is an asynchronous startup exception");
            }
        }

        private class ConfigureForCorsHandler : DelegatingHandler
        {
            public ConfigureForCorsHandler(HttpMessageHandler innerHandler) : base(innerHandler)
            {
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
