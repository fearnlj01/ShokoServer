using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Sentry;
using Shoko.Plugin.Abstractions.Enums;
using Shoko.Server.API.ActionFilters;
using Shoko.Server.API.Authentication;
using Shoko.Server.API.SignalR;
using Shoko.Server.API.SignalR.Aggregate;
using Shoko.Server.API.SignalR.Legacy;
using Shoko.Server.API.Swagger;
using Shoko.Server.API.v3.Helpers;
using Shoko.Server.API.v3.Models.Shoko;
using Shoko.Server.API.WebUI;
using Shoko.Server.Plugin;
using Shoko.Server.Utilities;
using File = System.IO.File;
using AniDBEmitter = Shoko.Server.API.SignalR.Aggregate.AniDBEmitter;
using ShokoEventEmitter = Shoko.Server.API.SignalR.Aggregate.ShokoEventEmitter;
using QueueEmitter = Shoko.Server.API.SignalR.Aggregate.QueueEmitter;
using AVDumpEmitter = Shoko.Server.API.SignalR.Aggregate.AVDumpEmitter;
using NetworkEmitter = Shoko.Server.API.SignalR.Aggregate.NetworkEmitter;
using LegacyAniDBEmitter = Shoko.Server.API.SignalR.Legacy.AniDBEmitter;
using LegacyShokoEventEmitter = Shoko.Server.API.SignalR.Legacy.ShokoEventEmitter;

namespace Shoko.Server.API;

public static class APIExtensions
{
    public static IServiceCollection AddAPI(this IServiceCollection services)
    {
        services.AddSingleton<LoggingEmitter>();
        services.AddSingleton<LegacyAniDBEmitter>();
        services.AddSingleton<LegacyShokoEventEmitter>();
        services.AddSingleton<AniDBEmitter>();
        services.AddSingleton<ShokoEventEmitter>();
        services.AddSingleton<AVDumpEmitter>();
        services.AddSingleton<NetworkEmitter>();
        services.AddSingleton<QueueEmitter>();
        services.AddScoped<FilterFactory>();
        services.AddScoped<WebUIFactory>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = CustomAuthOptions.DefaultScheme;
            options.DefaultChallengeScheme = CustomAuthOptions.DefaultScheme;
        }).AddScheme<CustomAuthOptions, CustomAuthHandler>(CustomAuthOptions.DefaultScheme, _ => { });

        services.AddAuthorization(auth =>
        {
            auth.AddPolicy("admin",
                policy => policy.Requirements.Add(new UserHandler(user => user.IsAdmin == 1)));
            auth.AddPolicy("init",
                policy => policy.Requirements.Add(new UserHandler(user =>
                    user.JMMUserID == 0 && user.Username == "init")));
        });

        services.AddSwaggerGen(
            options =>
            {
                // resolve the IApiVersionDescriptionProvider service
                // note: that we have to build a temporary service provider here because one has not been created yet
                var provider = services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>();

                // add a swagger document for each discovered API version
                // note: you might choose to skip or document deprecated API versions differently
                foreach (var description in provider.ApiVersionDescriptions.OrderByDescending(a => a.ApiVersion))
                {
                    options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description));
                }

                options.AddSecurityDefinition("ApiKey",
                    new OpenApiSecurityScheme()
                    {
                        Description = "Shoko API Key Header",
                        Name = "apikey",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = "apikey"
                    });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement()
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme, Id = "ApiKey"
                            }
                        },
                        Array.Empty<string>()
                    }
                });

                // add a custom operation filter which sets default values
                //options.OperationFilter<SwaggerDefaultValues>();

                // integrate xml comments
                //Locate the XML file being generated by ASP.NET...
                var xmlFile = "Shoko.Server.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath);
                }

                options.AddPlugins();

                options.SchemaFilter<EnumSchemaFilter<EpisodeType>>();
                options.SchemaFilter<EnumSchemaFilter<SeriesType>>();
                options.SchemaFilter<EnumSchemaFilter<RenamerSettingType>>();
                options.SchemaFilter<EnumSchemaFilter<CodeLanguage>>();
                options.SchemaFilter<EnumSchemaFilter<Filter.FilterExpressionHelp.FilterExpressionParameterType>>();

                options.CustomSchemaIds(GetTypeName);
            });
        services.AddSwaggerGenNewtonsoftSupport();
        services.AddSignalR(o => { o.EnableDetailedErrors = true; })
            .AddNewtonsoftJsonProtocol(o => o.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver());

        // allow CORS calls from other both local and non-local hosts
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder => { builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader(); });
        });

        // this caused issues with auth. https://stackoverflow.com/questions/43574552
        services.AddMvc(options =>
            {
                options.EnableEndpointRouting = false;
                options.AllowEmptyInputInBodyModelBinding = true;
                foreach (var formatter in options.InputFormatters)
                {
                    if (formatter.GetType() == typeof(NewtonsoftJsonInputFormatter))
                    {
                        ((NewtonsoftJsonInputFormatter)formatter).SupportedMediaTypes.Add(
                            MediaTypeHeaderValue.Parse("text/plain"));
                    }
                }

                options.Filters.Add(typeof(DatabaseBlockedFilter));
                options.Filters.Add(typeof(ServerNotRunningFilter));

                EmitEmptyEnumerableInsteadOfNullAttribute.MvcOptions = options;
            })
            .AddNewtonsoftJson(json =>
            {
                json.SerializerSettings.MaxDepth = 10;
                json.SerializerSettings.ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new DefaultNamingStrategy()
                };
                json.SerializerSettings.NullValueHandling = NullValueHandling.Include;
                json.SerializerSettings.DefaultValueHandling = DefaultValueHandling.Populate;
                // json.SerializerSettings.DateFormatString = "yyyy-MM-dd";
            })
            .AddPluginControllers()
            .AddControllersAsServices();

        services.AddApiVersioning(o =>
        {
            o.ReportApiVersions = true;
            o.AssumeDefaultVersionWhenUnspecified = true;
            o.ApiVersionReader = ApiVersionReader.Combine(
                new QueryStringApiVersionReader(),
                new HeaderApiVersionReader("api-version"),
                new ShokoApiReader()
            );
        });
        services.AddVersionedApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });
        services.AddResponseCaching();

        services.Configure<KestrelServerOptions>(options =>
        {
            options.AllowSynchronousIO = true;
        });
        return services;
    }

    private static string GetTypeName(Type type)
    {
        if (type.IsGenericType)
            return GetGenericTypeName(type);

        return string.Join(".", type.FullName.Replace("+", ".").Replace("`1", "").Split(".").TakeLast(2));
    }

    private static string GetGenericTypeName(Type genericType)
    {
        return genericType.Name.Replace("+", ".").Replace("`1", "") + "[" + string.Join(",", genericType.GetGenericArguments().Select(GetTypeName)) + "]";
    }

    private static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
    {
        var info = new OpenApiInfo
        {
            Title = $"Shoko API {description.ApiVersion}",
            Version = description.ApiVersion.ToString(),
            Description = "Shoko Server API."
        };

        if (description.IsDeprecated)
        {
            info.Description += " This API version has been deprecated.";
        }

        return info;
    }

    public static IApplicationBuilder UseAPI(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next.Invoke(context);
            }
            catch (Exception e)
            {
                try
                {
                    SentrySdk.CaptureException(e);
                }
                catch
                {
                    // ignore
                }
                throw;
            }
        });

#if DEBUG
        app.UseDeveloperExceptionPage();
#endif
        // Create web ui directory and add the bootstrapper.
        var webUIDir = new DirectoryInfo(Path.Combine(Utils.ApplicationPath, "webui"));
        if (!webUIDir.Exists)
        {
            webUIDir.Create();

            var backupDir = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
                "webui"));
            if (backupDir.Exists)
            {
                CopyFilesRecursively(backupDir, webUIDir);
            }
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new WebUiFileProvider(webUIDir.FullName),
            RequestPath = "/webui",
            ServeUnknownFileTypes = true,
            DefaultContentType = "text/html",
            OnPrepareResponse = ctx =>
            {
                var requestPath = ctx.File.PhysicalPath;
                // We set the cache headers only for index.html file because it doesn't have a different hash when changed
                if (requestPath?.EndsWith("index.html", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                    ctx.Context.Response.Headers.Append("Expires", "0");
                }
            }
        });

        app.UseSwagger(c =>
        {
            c.PreSerializeFilters.Add((swaggerDoc, _) => {
                var version = double.Parse(swaggerDoc.Info.Version);
                swaggerDoc.Servers.Add(new OpenApiServer{Url = $"/api/v{version:0}/"});

                var basepathInt = $"/api/v{version:0}/";
                var basepathDecimal = $"/api/v{version:0.0}/";
                var paths = new OpenApiPaths();
                foreach (var path in swaggerDoc.Paths)
                {
                    if (!path.Key.Contains(basepathInt) && !path.Key.Contains(basepathDecimal))
                    {
                        path.Value.Servers.Clear();
                        path.Value.Servers.Add(new OpenApiServer
                        {
                            Url = "/"
                        });
                    }

                    paths.Add(path.Key.Replace(basepathInt, "/").Replace(basepathDecimal, "/"), path.Value);
                }
                swaggerDoc.Paths = paths;
            });
        });
        app.UseSwaggerUI(
            options =>
            {
                // build a swagger endpoint for each discovered API version
                var provider = app.ApplicationServices.GetRequiredService<IApiVersionDescriptionProvider>();
                foreach (var description in provider.ApiVersionDescriptions.OrderByDescending(a => a.ApiVersion))
                {
                    options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                        description.GroupName.ToUpperInvariant());
                }
                options.EnablePersistAuthorization();
            });
        // Important for first run at least
        app.UseAuthentication();

        app.UseRouting();
        app.UseEndpoints(conf =>
        {
            conf.MapHub<AniDBHub>("/signalr/anidb");
            conf.MapHub<LoggingHub>("/signalr/logging");
            conf.MapHub<ShokoEventHub>("/signalr/shoko");
            conf.MapHub<AggregateHub>("/signalr/aggregate");
        });

        app.UseCors(options => options.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

        app.UseMvc();

        return app;
    }

    private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var dir in source.GetDirectories())
        {
            CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
        }

        foreach (var file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(target.FullName, file.Name));
        }
    }
}
