﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschraenkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Squidex.Domain.Apps.Entities.Rules.Commands;
using Squidex.Infrastructure;
using Squidex.Infrastructure.Commands;
using Squidex.Infrastructure.Log;

namespace Squidex.Domain.Apps.Entities.Rules.Indexes
{
    public sealed class RulesIndex : ICommandMiddleware, IRulesIndex
    {
        private readonly IGrainFactory grainFactory;

        public RulesIndex(IGrainFactory grainFactory)
        {
            Guard.NotNull(grainFactory, nameof(grainFactory));

            this.grainFactory = grainFactory;
        }

        public Task RebuildAsync(DomainId appId, HashSet<DomainId> rules)
        {
            return Index(appId).RebuildAsync(rules);
        }

        public async Task<List<IRuleEntity>> GetRulesAsync(DomainId appId)
        {
            using (Profiler.TraceMethod<RulesIndex>())
            {
                var ids = await GetRuleIdsAsync(appId);

                var rules =
                    await Task.WhenAll(
                        ids.Select(x => GetRuleAsync(appId, x)));

                return rules.NotNull().ToList();
            }
        }

        private async Task<IRuleEntity?> GetRuleAsync(DomainId appId, DomainId id)
        {
            using (Profiler.TraceMethod<RulesIndex>())
            {
                var rule = await GetRuleInternalAsync(appId, id);

                if (IsFound(rule))
                {
                    return rule;
                }

                return null;
            }
        }

        private async Task<List<DomainId>> GetRuleIdsAsync(DomainId appId)
        {
            using (Profiler.TraceMethod<RulesIndex>())
            {
                return await Index(appId).GetIdsAsync();
            }
        }

        public async Task HandleAsync(CommandContext context, NextDelegate next)
        {
            await next(context);

            if (context.IsCompleted)
            {
                switch (context.Command)
                {
                    case CreateRule createRule:
                        await CreateRuleAsync(createRule);
                        break;
                    case DeleteRule deleteRule:
                        await DeleteRuleAsync(deleteRule);
                        break;
                }
            }
        }

        private async Task CreateRuleAsync(CreateRule command)
        {
            await Index(command.AppId.Id).AddAsync(command.RuleId);
        }

        private async Task DeleteRuleAsync(DeleteRule command)
        {
            var rule = await GetRuleInternalAsync(command.AppId.Id, command.RuleId);

            if (IsFound(rule))
            {
                await Index(rule.AppId.Id).RemoveAsync(rule.Id);
            }
        }

        private async Task<IRuleEntity> GetRuleInternalAsync(DomainId appId, DomainId id)
        {
            var key = DomainId.Combine(appId, id).ToString();

            var rule = await grainFactory.GetGrain<IRuleGrain>(key).GetStateAsync();

            return rule.Value;
        }

        private IRulesByAppIndexGrain Index(DomainId appId)
        {
            return grainFactory.GetGrain<IRulesByAppIndexGrain>(appId.ToString());
        }

        private static bool IsFound(IRuleEntity rule)
        {
            return rule.Version > EtagVersion.Empty && !rule.IsDeleted;
        }
    }
}
