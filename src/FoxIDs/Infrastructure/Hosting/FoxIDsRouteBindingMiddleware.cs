﻿using FoxIDs.Models;
using FoxIDs.Repository;
using ITfoxtec.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using FoxIDs.Models.Config;
using StackExchange.Redis;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;

namespace FoxIDs.Infrastructure.Hosting
{
    public class FoxIDsRouteBindingMiddleware : RouteBindingMiddleware
    {
        private static Regex partyNameBindingRegex = new Regex(@"^(?:(?:(?<downparty>[\w-_]+)(?:\((?:(?:(?<toupparty>[\w-_]+)(?:,(?<toupparty>[\w-_]+))*)|(?<toupparty>\*))\))?)|(?:\((?<upparty>[\w-_]+)\))|(?:\~(?<upparty>[\w-_]+)\~)|(?:.(?<upparty>[\w-_]+).))$", RegexOptions.Compiled);
        private readonly FoxIDsSettings settings;
        private readonly ITenantRepository tenantRepository;
        private readonly IConnectionMultiplexer redisConnectionMultiplexer;
        private readonly TokenCredential tokenCredential;

        public FoxIDsRouteBindingMiddleware(RequestDelegate next, FoxIDsSettings settings, ITenantRepository tenantRepository, IConnectionMultiplexer redisConnectionMultiplexer, TokenCredential tokenCredential) : base(next, tenantRepository)
        {
            this.settings = settings;
            this.tenantRepository = tenantRepository;
            this.redisConnectionMultiplexer = redisConnectionMultiplexer;
            this.tokenCredential = tokenCredential;
        }

        protected override Track.IdKey GetTrackIdKey(string[] route)
        {
            if (route.Length == 0)
            {
                return null;
            }
            else if (route.Length >= 1 && route[0].Equals(Constants.Routes.DefaultSiteController, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }
            else if (route.Length > 2)
            {
                return new Track.IdKey
                {
                    TenantName = route[0].ToLower(),
                    TrackName = route[1].ToLower()
                };
            }
            else if (route.Length == 2)
            {
                throw new NotSupportedException($"FoxIDs route '{string.Join('/', route)}' without an action is not supported.");
            }
            else
            {
                throw new NotSupportedException($"FoxIDs route '{string.Join('/', route)}' is not supported.");
            }
        }

        protected override bool AcceptUnknownParty(string path, string[] route)
        {
            if(route.Length > 2)
            {
                if (path.EndsWith(IdentityConstants.OidcDiscovery.Path, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
                else if (path.EndsWith(IdentityConstants.OidcDiscovery.Keys, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
                else if (path.EndsWith($"{Constants.Routes.SamlController}/{Constants.Endpoints.SamlIdPMetadata}", StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
                else if (path.EndsWith($"{Constants.Routes.SamlController}/{Constants.Endpoints.SamlSPMetadata}", StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        protected override string GetPartyNameAndbinding(string[] route)
        {
            if (route.Length >= 3)
            {
                return route[2].ToLower();
            }
            else
            {
                return null;
            }
        }

        protected override async ValueTask<RouteBinding> PostRouteDataAsync(TelemetryScopedLogger scopedLogger, IServiceProvider requestServices, Track.IdKey trackIdKey, Track track, RouteBinding routeBinding, string partyNameAndBinding, bool acceptUnknownParty)
        {
            routeBinding.PartyNameAndBinding = partyNameAndBinding;
            routeBinding.Key = await LoadTrackKeyAsync(requestServices, trackIdKey, track);
            routeBinding.ClaimMappings = track.ClaimMappings;
            routeBinding.SequenceLifetime = track.SequenceLifetime;
            routeBinding.MaxFailingLogins = track.MaxFailingLogins;
            routeBinding.FailingLoginCountLifetime = track.FailingLoginCountLifetime;
            routeBinding.FailingLoginObservationPeriod = track.FailingLoginObservationPeriod;
            routeBinding.PasswordLength = track.PasswordLength;
            routeBinding.CheckPasswordComplexity = track.CheckPasswordComplexity.Value;
            routeBinding.CheckPasswordRisk = track.CheckPasswordRisk.Value;
            routeBinding.AllowIframeOnDomains = track.AllowIframeOnDomains;
            routeBinding.SendEmail = track.SendEmail;
            routeBinding.Logging = track.Logging;

            if (!partyNameAndBinding.IsNullOrWhiteSpace())
            {
                var partyNameBindingMatch = partyNameBindingRegex.Match(partyNameAndBinding);
                if (!partyNameBindingMatch.Success)
                {
                    throw new ArgumentException($"Invalid party name and binding match. PartyNameAndBinding '{partyNameAndBinding}'");
                }

                if (partyNameBindingMatch.Groups["upparty"].Success)
                {
                    routeBinding.UpParty = await GetUpPartyAsync(tenantRepository, trackIdKey, partyNameBindingMatch.Groups["upparty"], acceptUnknownParty);
                }
                else if (partyNameBindingMatch.Groups["downparty"].Success)
                {
                    routeBinding.DownParty = await GetDownPartyAsync(tenantRepository, trackIdKey, partyNameBindingMatch.Groups["downparty"], acceptUnknownParty);

                    if (routeBinding.DownParty?.AllowUpParties?.Count() >= 1)
                    {
                        if (partyNameBindingMatch.Groups["toupparty"].Success)
                        {
                            routeBinding.ToUpParties = await GetAllowedToUpPartyIds(scopedLogger, trackIdKey, partyNameBindingMatch.Groups["toupparty"], routeBinding.DownParty);
                        }
                        else
                        {
                            routeBinding.ToUpParties = routeBinding.DownParty.AllowUpParties.Take(1).ToList();
                            routeBinding.DefaultToUpParties = true;
                        }
                    }
                }
                else
                {
                    throw new ArgumentException($"Invalid party name and binding group match. PartyNameAndBinding '{partyNameAndBinding}'");
                }
            }

            return routeBinding;
        }

        private async Task<RouteTrackKey> LoadTrackKeyAsync(IServiceProvider requestServices, Track.IdKey trackIdKey, Track track)
        {
            switch (track.Key.Type)
            {
                case TrackKeyType.Contained:
                    return new RouteTrackKey
                    {
                        Type = track.Key.Type,
                        PrimaryKey = new RouteTrackKeyItem { Key = track.Key.Keys[0].Key },
                        SecondaryKey = track.Key.Keys.Count > 1 ? new RouteTrackKeyItem { Key = track.Key.Keys[1].Key } : null,
                    };

                case TrackKeyType.KeyVaultRenewSelfSigned:
                    var trackKeyExternal = await GetTrackKeyItemsAsync(trackIdKey.TenantName, trackIdKey.TrackName, track);
                    return new RouteTrackKey
                    {
                        Type = track.Key.Type,
                        ExternalName = track.Key.ExternalName,
                        PrimaryKey = new RouteTrackKeyItem { ExternalId = trackKeyExternal.Keys[0].ExternalId, Key = trackKeyExternal.Keys[0].Key },
                        SecondaryKey = trackKeyExternal.Keys.Count > 1 ? new RouteTrackKeyItem { ExternalId = trackKeyExternal.Keys[1].ExternalId, Key = trackKeyExternal.Keys[1].Key } : null,
                    };

                case TrackKeyType.KeyVaultUpload:
                default:
                    throw new Exception($"Track key type not supported '{track.Key.Type}'.");
            }
        }

        private async Task<UpParty> GetUpPartyAsync(ITenantRepository tenantRepository, Track.IdKey trackIdKey, Group upPartyGroup, bool acceptUnknownParty)
        {
            var upPartyIdKey = new Party.IdKey
            {
                TenantName = trackIdKey.TenantName,
                TrackName = trackIdKey.TrackName,
                PartyName = upPartyGroup.Value,
            };

            try
            {
                return await tenantRepository.GetUpPartyByNameAsync(upPartyIdKey, required: !acceptUnknownParty);
            }
            catch (Exception ex)
            {
                throw new RouteCreationException($"Invalid tenantName '{upPartyIdKey.TenantName}', trackName '{upPartyIdKey.TrackName}' and upPartyName '{upPartyIdKey.PartyName}'.", ex);
            }
        }

        private async Task<DownParty> GetDownPartyAsync(ITenantRepository tenantRepository, Track.IdKey trackIdKey, Group downPartyGroup, bool acceptUnknownParty)
        {
            var downPartyIdKey = new Party.IdKey
            {
                TenantName = trackIdKey.TenantName,
                TrackName = trackIdKey.TrackName,
                PartyName = downPartyGroup.Value,
            };

            try
            {
                return await tenantRepository.GetDownPartyByNameAsync(downPartyIdKey, required: !acceptUnknownParty);
            }
            catch (Exception ex)
            {
                throw new RouteCreationException($"Invalid tenantName '{downPartyIdKey.TenantName}', trackName '{downPartyIdKey.TrackName}' and downPartyName '{downPartyIdKey.PartyName}'.", ex);
            }
        }

        private async Task<List<UpPartyLink>> GetAllowedToUpPartyIds(TelemetryScopedLogger scopedLogger, Track.IdKey trackIdKey, Group toUpPartyGroup, DownParty downParty)
        {
            var toUpParties = new List<UpPartyLink>();
            if (toUpPartyGroup.Captures.Count > 5)
            {
                throw new ArgumentException($"More then 5 to up-party for down-party '{downParty.Id}'.");
            }

            foreach (Capture upPartyCapture in toUpPartyGroup.Captures)
            {
                var toUpPartyIdKey = new Party.IdKey
                {
                    TenantName = trackIdKey.TenantName,
                    TrackName = trackIdKey.TrackName,
                    PartyName = upPartyCapture.Value,
                };
                await toUpPartyIdKey.ValidateObjectAsync();

                var allowUpParty = downParty.AllowUpParties.Where(ap => ap.Name == toUpPartyIdKey.PartyName).SingleOrDefault();
                if (allowUpParty != null)
                {
                    toUpParties.Add(allowUpParty);
                }
                else
                {
                    try
                    {
                        throw new ArgumentException($"Up-party name '{toUpPartyIdKey.PartyName}' not allowed for down-party '{downParty.Id}',");
                    }
                    catch (Exception ex)
                    {
                        scopedLogger.Warning(ex);
                    }
                }
            }

            if (toUpParties.Count() > 1)
            {
                throw new NotSupportedException("Currently only 0 and 1 to up-party is supported.");
            }
            return toUpParties;
        }


        public async Task<TrackKeyExternal> GetTrackKeyItemsAsync(string tenantName, string trackName, Track track)
        {
            var key = RadisKey(tenantName, trackName, track.Key.ExternalName);
            var db = redisConnectionMultiplexer.GetDatabase();

            var trackKeyExternalValue = (string)await db.StringGetAsync(key);
            if (!trackKeyExternalValue.IsNullOrEmpty())
            {
                return trackKeyExternalValue.ToObject<TrackKeyExternal>();
            }

            var trackKeyExternal = await LoadTrackKeyExternalFromKeyVaultAsync(track);
            await db.StringSetAsync(key, trackKeyExternal.ToJson(), TimeSpan.FromSeconds(track.KeyExternalCacheLifetime));

            return trackKeyExternal;
        }

        private async Task<TrackKeyExternal> LoadTrackKeyExternalFromKeyVaultAsync(Track track)
        {
            var now = DateTimeOffset.UtcNow;
            var certificateClient = new CertificateClient(new Uri(settings.KeyVault.EndpointUri), tokenCredential);

            var externalKeys = new List<(string Id, DateTimeOffset? NotBefore)>();
            var certificateVersions = certificateClient.GetPropertiesOfCertificateVersionsAsync(track.Key.ExternalName);
            await foreach (var certificateVersion in certificateVersions)
            {
                if (certificateVersion.Enabled == true && certificateVersion.ExpiresOn >= now)
                {
                    externalKeys.Add((certificateVersion.Version, certificateVersion.NotBefore));
                }
            }

            if (externalKeys.Count <= 0)
            {
                throw new Exception($"Track key external certificate '{track.Key.ExternalName}' do not exist in Key Vault.");
            }

            var trackKeyExternal = new TrackKeyExternal();
            if (externalKeys.Count == 1)
            {
                trackKeyExternal.Keys = new List<TrackKeyExternalItem> { new TrackKeyExternalItem { ExternalId = externalKeys.First().Id } };
            }
            else
            {
                var topTwoExternalKeys = externalKeys.OrderByDescending(e => e.NotBefore).Take(2);
                var firstExternalKey = topTwoExternalKeys.First();
                if (firstExternalKey.NotBefore <= now.AddDays(-track.KeyExternalPrimaryAfterDays))
                {
                    trackKeyExternal.Keys = new List<TrackKeyExternalItem> { new TrackKeyExternalItem { ExternalId = firstExternalKey.Id }, new TrackKeyExternalItem { ExternalId = topTwoExternalKeys.Last().Id } };
                }
                else
                {
                    trackKeyExternal.Keys = new List<TrackKeyExternalItem> { new TrackKeyExternalItem { ExternalId = topTwoExternalKeys.Last().Id }, new TrackKeyExternalItem { ExternalId = firstExternalKey.Id } };
                }
            }

            foreach (var keyItem in trackKeyExternal.Keys)
            {
                var certificateWithPolicy = certificateClient.GetCertificateVersion(track.Key.ExternalName, keyItem.ExternalId);
                var certificateRawValue = certificateWithPolicy?.Value?.Cer;
                if (certificateRawValue == null)
                {
                    throw new Exception($"Track key external certificate '{track.Key.ExternalName}' version '{keyItem.ExternalId}' from Key Vault is null.");
                }
                keyItem.Key = await new X509Certificate2(certificateRawValue).ToFTJsonWebKeyAsync();
            }
            return trackKeyExternal;
        }

        private string RadisKey(string tenantName, string trackName, string name)
        {
            return $"track_key_ext_{tenantName}_{trackName}_{name}";
        }
    }
}
