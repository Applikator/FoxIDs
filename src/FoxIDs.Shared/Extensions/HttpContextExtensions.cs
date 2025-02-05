﻿using FoxIDs.Models.Config;
using ITfoxtec.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using ITfoxtec.Identity.Util;
using FoxIDs.Models;

namespace FoxIDs
{
    public static class HttpContextExtensions
    {
        public static string GetHost(this HttpContext context, bool addTrailingSlash = true)
        {
            var routeBinding = context.GetRouteBinding();
            if (routeBinding != null && !routeBinding.UseCustomDomain)
            {
                var settings = context.RequestServices.GetService<Settings>();
                if (settings != null)
                {
                    if (!settings.FoxIDsControlEndpoint.IsNullOrEmpty())
                    {
                        return AddSlash(settings.FoxIDsControlEndpoint, addTrailingSlash);
                    }
                    if (!settings.FoxIDsEndpoint.IsNullOrEmpty())
                    {
                        return AddSlash(settings.FoxIDsEndpoint, addTrailingSlash);
                    }
                }
            }

            return AddSlash($"{context.Request.Scheme}://{context.Request.Host.ToUriComponent()}/", addTrailingSlash);
        }

        public static string GetHostWithTenantAndTrack(this HttpContext context, string trackName = null)
        {
            var routeBinding = context.GetRouteBinding();         
            if (!GetUseCustomDomainConsideringTrackName(routeBinding, trackName))
            {
                return UrlCombine.Combine(context.GetHost(), routeBinding.TenantName, trackName ?? routeBinding.TrackName);
            }
            else
            {
                return UrlCombine.Combine(context.GetHost(), trackName ?? routeBinding.TrackName);
            }
        }

        private static bool GetUseCustomDomainConsideringTrackName(RouteBinding routeBinding, string trackName)
        {
            if (!trackName.IsNullOrEmpty())
            {
                if (trackName.Equals(Constants.Routes.MasterTrackName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else
                {
                    return routeBinding.HasCustomDomain;
                }
            }

            return routeBinding.UseCustomDomain;
        }

        public static Uri GetHostUri(this HttpContext context)
        {
            return new Uri(context.GetHost());
        }

        private static string AddSlash(string url, bool addTrailingSlash = true)
        {
            if (url.EndsWith('/'))
            {
                return addTrailingSlash ? url : url.Substring(0, url.Length - 1);
            }
            else
            {
                return addTrailingSlash ? $"{url}/" : url;
            }
        }
    }
}
