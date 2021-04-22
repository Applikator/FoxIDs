﻿using FoxIDs.Infrastructure;
using FoxIDs.Models.Config;
using FoxIDs.Models.Sequences;
using ITfoxtec.Identity;
using ITfoxtec.Identity.Util;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoxIDs.Logic
{
    public class SequenceLogic : LogicBase
    {
        private readonly FoxIDsSettings settings;
        private readonly TelemetryScopedLogger logger;
        private readonly IDataProtectionProvider dataProtectionProvider;
        private readonly IDistributedCache distributedCache;
        private readonly LocalizationLogic localizationLogic;

        public SequenceLogic(FoxIDsSettings settings, TelemetryScopedLogger logger, IDataProtectionProvider dataProtectionProvider, IDistributedCache distributedCache, LocalizationLogic localizationLogic, IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
        {
            this.settings = settings;
            this.logger = logger;
            this.dataProtectionProvider = dataProtectionProvider;
            this.distributedCache = distributedCache;
            this.localizationLogic = localizationLogic;
        }

        public async Task StartSequenceAsync(bool setStart)
        {
            try
            {
                (var sequenceString, var sequence) = await StartSeparateSequenceAsync();

                HttpContext.Items[Constants.Sequence.Object] = sequence;
                HttpContext.Items[Constants.Sequence.String] = sequenceString;
                if (setStart)
                {
                    HttpContext.Items[Constants.Sequence.Start] = true;
                }
            }
            catch (SequenceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SequenceException("Unable to start sequence.", ex);
            }
        }

        public async Task<(string sequenceString, Sequence sequence)> StartSeparateSequenceAsync(bool? accountAction = null, Sequence currentSequence = null, bool requireeUiUpPartyId = false)
        {
            try
            {
                var sequence = new Sequence
                {
                    Id = RandomGenerator.Generate(12),
                    CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    AccountAction = accountAction
                };

                if(currentSequence?.Culture?.IsNullOrEmpty() == false)
                {
                    sequence.Culture = currentSequence.Culture;
                }

                if(currentSequence?.UiUpPartyId.IsNullOrEmpty() == false)
                {
                    sequence.UiUpPartyId = currentSequence.UiUpPartyId;
                }
                else if (requireeUiUpPartyId)
                {
                    throw new Exception("Required UiUpPartyId is null or empty.");
                }

                var sequenceString = await CreateSequenceStringAsync(sequence);

                logger.ScopeTrace(() => $"Sequence started, id '{sequence.Id}'.", new Dictionary<string, string> { { "sequenceId", sequence.Id }, { "accountAction", accountAction == true ? "true" : "false" } });
                return (sequenceString: sequenceString, sequence: sequence);
            }
            catch (Exception ex)
            {
                throw new SequenceException("Unable to start sequence.", ex);
            }
        }

        public async Task SetCultureAsync(IEnumerable<string> names)
        {
            var culture = localizationLogic.GetSupportedCulture(names);
            if(!culture.IsNullOrEmpty())
            {
                var sequence = HttpContext.GetSequence();
                sequence.Culture = culture;
                HttpContext.Items[Constants.Sequence.Object] = sequence;
                HttpContext.Items[Constants.Sequence.String] = await CreateSequenceStringAsync(sequence);

                logger.ScopeTrace(() => $"Sequence culture added, id '{sequence.Id}', culture '{culture}'.");
            }
        }
        public async Task SetUiUpPartyIdAsync(string uiUpPartyId)
        {
            if(!uiUpPartyId.IsNullOrEmpty())
            {
                var sequence = HttpContext.GetSequence();
                sequence.UiUpPartyId = uiUpPartyId;
                HttpContext.Items[Constants.Sequence.Object] = sequence;
                HttpContext.Items[Constants.Sequence.String] = await CreateSequenceStringAsync(sequence);

                logger.ScopeTrace(() => $"Sequence culture added, id '{sequence.Id}', UiUpPartyId '{uiUpPartyId}'.");
            }
        }

        public Task<Sequence> TryReadSequenceAsync(string sequenceString)
        {
            if (!sequenceString.IsNullOrWhiteSpace())
            {
                sequenceString.ValidateMaxLength(Constants.Sequence.MaxLength, nameof(sequenceString), nameof(ValidateSequenceAsync));

                try
                {
                    var sequence = CreateProtector().Unprotect(sequenceString).ToObject<Sequence>();
                    if (sequence != null)
                    {
                        logger.SetScopeProperty(Constants.Logs.SequenceId, sequence.Id);
                    }
                    return Task.FromResult(sequence);
                }
                catch
                { }
            }
            return Task.FromResult<Sequence>(null);
        }

        public async Task ValidateSequenceAsync(string sequenceString, bool setValid = false)
        {
            if (sequenceString.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(sequenceString));
            sequenceString.ValidateMaxLength(Constants.Sequence.MaxLength, nameof(sequenceString), nameof(ValidateSequenceAsync));

            try
            {
                var sequence = await Task.FromResult(CreateProtector().Unprotect(sequenceString).ToObject<Sequence>());
                CheckTimeout(sequence);
                HttpContext.Items[Constants.Sequence.Object] = sequence;
                HttpContext.Items[Constants.Sequence.String] = sequenceString;
                if (setValid)
                {
                    HttpContext.Items[Constants.Sequence.Valid] = true;
                }

                logger.ScopeTrace(() => $"Sequence is validated, id '{sequence.Id}'.", new Dictionary<string, string> { { "sequenceId", sequence.Id }, { "accountAction", sequence.AccountAction == true ? "true" : "false" } });
            }
            catch (SequenceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SequenceException("Invalid sequence.", ex);
            }
        }

        public async Task<T> SaveSequenceDataAsync<T>(T data, Sequence sequence = null) where T : ISequenceData
        {
            await data.ValidateObjectAsync();

            sequence = sequence ?? HttpContext.GetSequence();
            var absoluteExpiration = DateTimeOffset.FromUnixTimeSeconds(sequence.CreateTime).AddSeconds(sequence.AccountAction == true ? settings.AccountActionSequenceLifetime : HttpContext.GetRouteBinding().SequenceLifetime);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = absoluteExpiration
            };
            await distributedCache.SetStringAsync(DataKey(typeof(T), sequence), data.ToJson(), options);

            return data;
        }

        public async Task<T> GetSequenceDataAsync<T>(bool remove = true, bool allowNull = false) where T : ISequenceData
        {
            var sequence = HttpContext.GetSequence(allowNull);
            if(allowNull && sequence == null)
            {
                return default;
            }
            var key = DataKey(typeof(T), sequence);
            var data = await distributedCache.GetStringAsync(key);
            if(data == null)
            {
                if(allowNull)
                {
                    return default;
                }
                else
                {
                    throw new SequenceException($"Cache do not contain the sequence object with sequence id '{sequence.Id}'.");
                }
            }

            if(remove)
            {
                await distributedCache.RemoveAsync(key);
            }

            return data.ToObject<T>();
        }

        public async Task RemoveSequenceDataAsync<T>() where T : ISequenceData
        {
            var sequence = HttpContext.GetSequence();
            var key = DataKey(typeof(T), sequence);
            await distributedCache.RemoveAsync(key);
        }

        private Task<string> CreateSequenceStringAsync(Sequence sequence)
        {
            return Task.FromResult(CreateProtector().Protect(sequence.ToJson()));
        }

        private string DataKey(Type type, Sequence sequence)
        {
            var routeBinding = HttpContext.GetRouteBinding();
            return $"{routeBinding.TenantName}.{routeBinding.TrackName}.seq.{type.Name.ToLower()}.{sequence.Id}.{sequence.CreateTime}";
        }

        private IDataProtector CreateProtector()
        {
            var routeBinding = HttpContext.GetRouteBinding();
            return dataProtectionProvider.CreateProtector(new[] { routeBinding.TenantName, routeBinding.TrackName, typeof(SequenceLogic).Name });
        }

        private void CheckTimeout(Sequence sequence) 
        {
            var now = DateTimeOffset.UtcNow;
            var createTime = DateTimeOffset.FromUnixTimeSeconds(sequence.CreateTime);

            if(sequence.AccountAction == true)
            {
                if (createTime.AddSeconds(settings.AccountActionSequenceLifetime) < now)
                {
                    throw new SequenceTimeoutException($"Sequence timeout, id '{sequence.Id}'.") { AccountAction = sequence.AccountAction, SequenceLifetime = settings.AccountActionSequenceLifetime };
                }
            }
            else
            {
                if (createTime.AddSeconds(HttpContext.GetRouteBinding().SequenceLifetime) < now)
                {
                    throw new SequenceTimeoutException($"Sequence timeout, id '{sequence.Id}'.") { AccountAction = sequence.AccountAction, SequenceLifetime = HttpContext.GetRouteBinding().SequenceLifetime };
                }
            }
        }
    }
}
