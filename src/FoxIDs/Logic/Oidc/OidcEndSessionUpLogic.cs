﻿using FoxIDs.Infrastructure;
using FoxIDs.Models;
using FoxIDs.Models.Logic;
using FoxIDs.Models.Sequences;
using FoxIDs.Repository;
using ITfoxtec.Identity;
using ITfoxtec.Identity.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using UrlCombineLib;

namespace FoxIDs.Logic
{
    public class OidcEndSessionUpLogic<TParty, TClient> : LogicBase where TParty : OidcUpParty<TClient> where TClient : OidcUpClient
    {
        private readonly TelemetryScopedLogger logger;
        private readonly IServiceProvider serviceProvider;
        private readonly ITenantRepository tenantRepository;
        private readonly SequenceLogic sequenceLogic;
        private readonly SessionUpPartyLogic sessionUpPartyLogic;
        private readonly SingleLogoutDownLogic singleLogoutDownLogic;
        private readonly OAuthRefreshTokenGrantDownLogic<OAuthDownClient, OAuthDownScope, OAuthDownClaim> oauthRefreshTokenGrantLogic;
        private readonly FormActionLogic formActionLogic;

        public OidcEndSessionUpLogic(TelemetryScopedLogger logger, IServiceProvider serviceProvider, ITenantRepository tenantRepository, SequenceLogic sequenceLogic, SessionUpPartyLogic sessionUpPartyLogic, SingleLogoutDownLogic singleLogoutDownLogic, OAuthRefreshTokenGrantDownLogic<OAuthDownClient, OAuthDownScope, OAuthDownClaim> oauthRefreshTokenGrantLogic, FormActionLogic formActionLogic, IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.tenantRepository = tenantRepository;
            this.sequenceLogic = sequenceLogic;
            this.sessionUpPartyLogic = sessionUpPartyLogic;
            this.singleLogoutDownLogic = singleLogoutDownLogic;
            this.oauthRefreshTokenGrantLogic = oauthRefreshTokenGrantLogic;
            this.formActionLogic = formActionLogic;
        }
        public async Task<IActionResult> EndSessionRequestRedirectAsync(UpPartyLink partyLink, LogoutRequest logoutRequest)
        {
            logger.ScopeTrace("Up, OIDC End session request redirect.");
            var partyId = await UpParty.IdFormatAsync(RouteBinding, partyLink.Name);
            logger.SetScopeProperty("upPartyId", partyId);

            await logoutRequest.ValidateObjectAsync();

            await sequenceLogic.SaveSequenceDataAsync(new OidcUpSequenceData
            {
                DownPartyId = logoutRequest.DownParty.Id,
                DownPartyType = logoutRequest.DownParty.Type,
                UpPartyId = partyId,
                SessionId = logoutRequest.SessionId,
                RequireLogoutConsent = logoutRequest.RequireLogoutConsent,
                PostLogoutRedirect = logoutRequest.PostLogoutRedirect,
            });

            return HttpContext.GetUpPartyUrl(partyLink.Name, Constants.Routes.OAuthUpJumpController, Constants.Endpoints.UpJump.EndSessionRequest, includeSequence: true).ToRedirectResult();
        }

        public async Task<IActionResult> EndSessionRequestAsync(string partyId)
        {
            logger.ScopeTrace("Up, OIDC End session request.");
            var oidcUpSequenceData = await sequenceLogic.GetSequenceDataAsync<OidcUpSequenceData>(remove: false);
            if (!oidcUpSequenceData.UpPartyId.Equals(partyId, StringComparison.Ordinal))
            {
                throw new Exception("Invalid up-party id.");
            }
            logger.SetScopeProperty("upPartyId", oidcUpSequenceData.UpPartyId);

            var party = await tenantRepository.GetAsync<OidcUpParty>(oidcUpSequenceData.UpPartyId);
            logger.SetScopeProperty("upPartyClientId", party.Client.ClientId);
            ValidatePartyLogoutSupport(party);

            var postLogoutRedirectUrl = HttpContext.GetUpPartyUrl(party.Name, Constants.Routes.OAuthController, Constants.Endpoints.EndSessionResponse, partyBindingPattern: party.PartyBindingPattern);
            var endSessionRequest = new EndSessionRequest
            {
                PostLogoutRedirectUri = postLogoutRedirectUrl,
                State = SequenceString
            };
            var session = await sessionUpPartyLogic.GetSessionAsync(party);
            if(session != null)
            {
                endSessionRequest.IdTokenHint = session.IdToken;
                try
                {
                    if (!oidcUpSequenceData.SessionId.Equals(session.SessionId, StringComparison.Ordinal))
                    {
                        throw new Exception("Requested session ID do not match up-party session ID.");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning(ex);
                }
            }
            logger.ScopeTrace($"End session request '{endSessionRequest.ToJsonIndented()}'.");

            await oauthRefreshTokenGrantLogic.DeleteRefreshTokenGrantsAsync(oidcUpSequenceData.SessionId);

            formActionLogic.AddFormActionAllowAll();

            var nameValueCollection = endSessionRequest.ToDictionary();
            logger.ScopeTrace($"End session request URL '{party.Client.EndSessionUrl}'.");
            logger.ScopeTrace("Up, Sending OIDC End session request.", triggerEvent: true);
            return await nameValueCollection.ToRedirectResultAsync(party.Client.EndSessionUrl);
        }

        private void ValidatePartyLogoutSupport(OidcUpParty party)
        {
            if (party.Client.EndSessionUrl.IsNullOrEmpty())
            {
                throw new EndpointException("End session not configured.") { RouteBinding = RouteBinding };
            }
        }

        public async Task<IActionResult> EndSessionResponseAsync(string partyId)
        {
            logger.ScopeTrace($"Up, OIDC End session response.");
            logger.SetScopeProperty("upPartyId", partyId);

            var party = await tenantRepository.GetAsync<OidcUpParty>(partyId);
            logger.SetScopeProperty("upPartyClientId", party.Client.ClientId);

            var queryDictionary = HttpContext.Request.Query.ToDictionary();
            var endSessionResponse = queryDictionary.ToObject<EndSessionResponse>();
            logger.ScopeTrace($"End session response '{endSessionResponse.ToJsonIndented()}'.");
            if (endSessionResponse.State.IsNullOrEmpty()) throw new ArgumentNullException(nameof(endSessionResponse.State), endSessionResponse.GetTypeName());

            await sequenceLogic.ValidateSequenceAsync(endSessionResponse.State);
            var sequenceData = await sequenceLogic.GetSequenceDataAsync<OidcUpSequenceData>(remove: party.DisableSingleLogout);

            var session = await sessionUpPartyLogic.DeleteSessionAsync();
            logger.ScopeTrace("Up, Successful OIDC End session response.", triggerEvent: true);

            if (party.DisableSingleLogout)
            {
                return await LogoutResponseDownAsync(sequenceData);
            }
            else
            {
                return await singleLogoutDownLogic.StartSingleLogoutAsync(
                    new UpPartyLink { Name = party.Name, Type = party.Type },
                    sequenceData.DownPartyId == null || sequenceData.DownPartyType == null ? null : new DownPartyLink { Id = sequenceData.DownPartyId, Type = sequenceData.DownPartyType.Value }, 
                    session);
            }
        }

        public async Task<IActionResult> SingleLogoutDone()
        {
            var sequenceData = await sequenceLogic.GetSequenceDataAsync<OidcUpSequenceData>(remove: true);
            return await LogoutResponseDownAsync(sequenceData);
        }

        private async Task<IActionResult> LogoutResponseDownAsync(OidcUpSequenceData sequenceData)
        {
            try
            {
                logger.ScopeTrace($"Response, Down type {sequenceData.DownPartyType}.");
                switch (sequenceData.DownPartyType)
                {
                    case PartyTypes.OAuth2:
                        throw new NotImplementedException();
                    case PartyTypes.Oidc:
                        return await serviceProvider.GetService<OidcEndSessionDownLogic<OidcDownParty, OidcDownClient, OidcDownScope, OidcDownClaim>>().EndSessionResponseAsync(sequenceData.DownPartyId);
                    case PartyTypes.Saml2:
                        return await serviceProvider.GetService<SamlLogoutDownLogic>().LogoutResponseAsync(sequenceData.DownPartyId, sessionIndex: sequenceData.SessionId);

                    default:
                        throw new NotSupportedException();
                }
            }
            catch (Exception ex)
            {
                throw new StopSequenceException("Falling logout response down", ex);
            }
        }
    }
}
