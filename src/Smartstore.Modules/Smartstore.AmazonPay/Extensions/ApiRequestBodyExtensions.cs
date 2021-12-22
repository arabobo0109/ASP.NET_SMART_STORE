﻿using System.Text.RegularExpressions;
using Amazon.Pay.API.Types;
using Newtonsoft.Json;

namespace Smartstore.AmazonPay
{
    internal static class ApiRequestBodyExtensions
    {
        /// <summary>
        /// This is a temporary workaround for the following SDK issue:
        /// https://github.com/amzn/amazon-pay-api-sdk-dotnet/issues/11
        /// </summary>
        public static string ToJsonNoType(this ApiRequestBody body)
        {
            // INFO: (mg) (core) I know this name is ugly, but "As*" should only be used for type casts where if B *IS* A already.
            // INFO: (mg) (core) I believe it is partly our fault that the inbuilt .ToJson() does not work: we build default serializer settings in CoreStarter (line 41).

            var serializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None
            };

            var jsonString = JsonConvert.SerializeObject(body, serializerSettings);

            // remove empty objects from the JSON string
            var regex = new Regex(",?\"[a-z]([a-z]|[A-Z])+\":{}");
            jsonString = regex.Replace(jsonString, string.Empty);

            // remove potential clutter
            var regex2 = new Regex("{,\"");
            jsonString = regex2.Replace(jsonString, "{\"");

            return jsonString;
        }
    }
}
