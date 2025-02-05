﻿using FoxIDs.Infrastructure;
using FoxIDs.Models;
using Api = FoxIDs.Models.Api;
using FoxIDs.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using AutoMapper;
using FoxIDs.Logic;

namespace FoxIDs.Controllers
{
    /// <summary>
    /// Login up-party API.
    /// </summary>
    public class TLoginUpPartyController : GenericPartyApiController<Api.LoginUpParty, Api.OAuthClaimTransform, LoginUpParty>
    {
        private readonly ValidateLoginPartyLogic validateLoginPartyLogic;

        public TLoginUpPartyController(TelemetryScopedLogger logger, IMapper mapper, ITenantRepository tenantRepository, DownPartyCacheLogic downPartyCacheLogic, UpPartyCacheLogic upPartyCacheLogic, DownPartyAllowUpPartiesQueueLogic downPartyAllowUpPartiesQueueLogic, ValidateGenericPartyLogic validateGenericPartyLogic, ValidateLoginPartyLogic validateLoginPartyLogic) : base(logger, mapper, tenantRepository, downPartyCacheLogic, upPartyCacheLogic, downPartyAllowUpPartiesQueueLogic, validateGenericPartyLogic)
        {
            this.validateLoginPartyLogic = validateLoginPartyLogic;
        }

        /// <summary>
        /// Get Login up-party.
        /// </summary>
        /// <param name="name">Party name.</param>
        /// <returns>Login up-party.</returns>
        [ProducesResponseType(typeof(Api.LoginUpParty), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Api.LoginUpParty>> GetLoginUpParty(string name) => await Get(name);

        /// <summary>
        /// Create Login up-party.
        /// </summary>
        /// <param name="party">Login up-party.</param>
        /// <returns>Login up-party.</returns>
        [ProducesResponseType(typeof(Api.LoginUpParty), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<Api.LoginUpParty>> PostLoginUpParty([FromBody] Api.LoginUpParty party) => await Post(party, ap => new ValueTask<bool>(validateLoginPartyLogic.ValidateApiModel(ModelState, ap)), (ap, mp) => new ValueTask<bool>(true));

        /// <summary>
        /// Update Login up-party.
        /// </summary>
        /// <param name="party">Login up-party.</param>
        /// <returns>Login up-party.</returns>
        [ProducesResponseType(typeof(Api.LoginUpParty), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Api.LoginUpParty>> PutLoginUpParty([FromBody] Api.LoginUpParty party) => await Put(party, ap => new ValueTask<bool>(validateLoginPartyLogic.ValidateApiModel(ModelState, ap)), (ap, mp) => new ValueTask<bool>(true));

        /// <summary>
        /// Delete Login up-party.
        /// </summary>
        /// <param name="name">Party name.</param>
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteLoginUpParty(string name) => await Delete(name);
    }
}
