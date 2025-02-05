﻿using AutoMapper;
using FoxIDs.Infrastructure;
using FoxIDs.Repository;
using Api = FoxIDs.Models.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Net;
using FoxIDs.Logic;
using System.Security.Claims;
using System.Collections.Generic;
using ITfoxtec.Identity;
using System;

namespace FoxIDs.Controllers
{
    public class TUserController : TenantApiController
    {
        private readonly TelemetryScopedLogger logger;
        private readonly IMapper mapper;
        private readonly ITenantRepository tenantRepository;
        private readonly BaseAccountLogic accountLogic;
        private readonly ExternalSecretLogic externalSecretLogic;

        public TUserController(TelemetryScopedLogger logger, IMapper mapper, ITenantRepository tenantRepository, BaseAccountLogic accountLogic, ExternalSecretLogic externalSecretLogic) : base(logger)
        {
            this.logger = logger;
            this.mapper = mapper;
            this.tenantRepository = tenantRepository;
            this.accountLogic = accountLogic;
            this.externalSecretLogic = externalSecretLogic;
        }

        /// <summary>
        /// Get user.
        /// </summary>
        /// <param name="email">User email.</param>
        /// <returns>User.</returns>
        [ProducesResponseType(typeof(Api.User), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Api.User>> GetUser(string email)
        {
            try
            {
                if (!ModelState.TryValidateRequiredParameter(email, nameof(email))) return BadRequest(ModelState);

                var mUser = await tenantRepository.GetAsync<Models.User>(await Models.User.IdFormatAsync(RouteBinding, email?.ToLower()));
                return Ok(mapper.Map<Api.User>(mUser));
            }
            catch (CosmosDataException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.Warning(ex, $"NotFound, Get '{typeof(Api.User).Name}' by email '{email}'.");
                    return NotFound(typeof(Api.User).Name, email);
                }
                throw;
            }
        }

        /// <summary>
        /// Create user.
        /// </summary>
        /// <param name="createUserRequest">User.</param>
        /// <returns>User.</returns>
        [ProducesResponseType(typeof(Api.User), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<Api.User>> PostUser([FromBody] Api.CreateUserRequest createUserRequest)
        {
            try
            {
                if (!await ModelState.TryValidateObjectAsync(createUserRequest)) return BadRequest(ModelState);
                createUserRequest.Email = createUserRequest.Email?.ToLower();

                var claims = new List<Claim>();
                if (createUserRequest.Claims?.Count > 0)
                {
                    foreach (var claimAndValue in createUserRequest.Claims)
                    {
                        foreach(var value in claimAndValue.Values)
                        {
                            claims.Add(new Claim(claimAndValue.Claim, value));
                        }
                    }
                }
                var mUser = await accountLogic.CreateUser(createUserRequest.Email, createUserRequest.Password, changePassword: createUserRequest.ChangePassword, claims: claims, 
                    confirmAccount: createUserRequest.ConfirmAccount, emailVerified: createUserRequest.EmailVerified, disableAccount: createUserRequest.DisableAccount, requireMultiFactor: createUserRequest.RequireMultiFactor);
                return Created(mapper.Map<Api.User>(mUser));
            }
            catch(UserExistsException ueex)
            {
                logger.Warning(ueex, $"Conflict, Create '{typeof(Api.User).Name}' by email '{createUserRequest.Email}'.");
                return Conflict(ueex.Message);
            }
            catch (AccountException aex)
            {
                ModelState.TryAddModelError(nameof(createUserRequest.Password), aex.Message);
                return BadRequest(ModelState, aex);
            }            
            catch (CosmosDataException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    logger.Warning(ex, $"Conflict, Create '{typeof(Api.User).Name}' by email '{createUserRequest.Email}'.");
                    return Conflict(typeof(Api.User).Name, createUserRequest.Email, nameof(createUserRequest.Email));
                }
                throw;
            }
        }

        /// <summary>
        /// Update user.
        /// </summary>
        /// <param name="user">User.</param>
        /// <returns>User.</returns>
        [ProducesResponseType(typeof(Api.User), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Api.User>> PutUser([FromBody] Api.UserRequest user)
        {
            try
            {
                if (!await ModelState.TryValidateObjectAsync(user)) return BadRequest(ModelState);
                user.Email = user.Email?.ToLower();

                var mUser = await tenantRepository.GetAsync<Models.User>(await Models.User.IdFormatAsync(RouteBinding, user.Email));

                mUser.ConfirmAccount = user.ConfirmAccount;
                mUser.EmailVerified = user.EmailVerified;
                mUser.ChangePassword = user.ChangePassword;
                mUser.DisableAccount = user.DisableAccount;
                if (!user.ActiveTwoFactorApp)
                {
                    if (!mUser.TwoFactorAppSecretExternalName.IsNullOrEmpty())
                    {
                        try
                        {
                            await externalSecretLogic.DeleteExternalSecretAsync(mUser.TwoFactorAppSecretExternalName);
                        }
                        catch (Exception ex)
                        {
                            logger.Warning(ex, $"Unable to delete external secret, secretExternalName '{mUser.TwoFactorAppSecretExternalName}'.");
                        }
                    }

                    mUser.TwoFactorAppSecretExternalName = null;
                    mUser.TwoFactorAppRecoveryCode = null;
                }
                mUser.RequireMultiFactor = user.RequireMultiFactor;
                var mClaims = mapper.Map<List<Models.ClaimAndValues>>(user.Claims);
                mUser.Claims = mClaims;
                await tenantRepository.UpdateAsync(mUser);

                return Ok(mapper.Map<Api.User>(mUser));
            }
            catch (CosmosDataException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.Warning(ex, $"NotFound, Update '{typeof(Api.UserRequest).Name}' by email '{user.Email}'.");
                    return NotFound(typeof(Api.UserRequest).Name, user.Email, nameof(user.Email));
                }
                throw;
            }
        }

        /// <summary>
        /// Delete user.
        /// </summary>
        /// <param name="email">User email.</param>
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteUser(string email)
        {
            try
            {
                if (!ModelState.TryValidateRequiredParameter(email, nameof(email))) return BadRequest(ModelState);
                email = email?.ToLower();

                await tenantRepository.DeleteAsync<Models.User>(await Models.User.IdFormatAsync(RouteBinding, email));
                return NoContent();
            }
            catch (CosmosDataException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.Warning(ex, $"NotFound, Delete '{typeof(Api.User).Name}' by email '{email}'.");
                    return NotFound(typeof(Api.User).Name, email);
                }
                throw;
            }
        }
    }
}
