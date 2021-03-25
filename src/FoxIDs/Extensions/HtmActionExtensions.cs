﻿using ITfoxtec.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoxIDs
{
    /// <summary>
    /// Extension methods for HTML form and redirect actions.
    /// </summary>
    public static class HtmActionExtensions
    {
        private const string title = "FoxIDs";

        /// <summary>
        /// Converts a Dictionary&lt;string, string&gt; to a HTML Post ContentResult.
        /// </summary>
        public static Task<ContentResult> ToHtmlPostContentResultAsync(this Dictionary<string, string> items, string url)
        {
            return Task.FromResult(new ContentResult
            {
                ContentType = "text/html",
                Content = items.ToHtmlPostPage(url, title: title),
            });
        }

        /// <summary>
        /// Converts a URL to a redirect ContentResult.
        /// </summary>
        public static ContentResult ToRedirectResult(this string url)
        {
            return new ContentResult
            {
                ContentType = "text/html",
                Content = url.HtmRedirectActionPage(title: title),
            };
        }


        /// <summary>
        /// Converts a Dictionary&lt;string, string&gt; to a redirect ContentResult.
        /// </summary>
        public static Task<ContentResult> ToRedirectResultAsync(this Dictionary<string, string> items, string url)
        {
            return Task.FromResult(new ContentResult
            {
                ContentType = "text/html",
                Content = items.ToHtmlGetPage(url, title: title),
            });
        }

        /// <summary>
        /// Converts a Dictionary&lt;string, string&gt; to a fragment ContentResult.
        /// </summary>
        public static Task<ContentResult> ToFragmentResultAsync(this Dictionary<string, string> items, string url)
        {
            return Task.FromResult(new ContentResult
            {
                ContentType = "text/html",
                Content = items.ToHtmlFragmentPage(url, title: title),
            });
        }
    }
}
