﻿// The MIT License (MIT)
// 
// Copyright (c) 2015-2019 Rasmus Mikkelsen
// Copyright (c) 2015-2019 eBay Software Foundation
// https://github.com/eventflow/EventFlow
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventFlow.Aggregates;
using EventFlow.Configuration;
using EventFlow.Configuration.Cancellation;
using EventFlow.Core;
using EventFlow.Jobs;
using EventFlow.Provided.Jobs;
using EventFlow.PublishRecovery;
using EventFlow.ReadStores;
using EventFlow.Sagas;

namespace EventFlow.Subscribers
{
    public class DomainEventPublisher : IDomainEventPublisher
    {
        private readonly IDispatchToEventSubscribers _dispatchToEventSubscribers;
        private readonly IDispatchToSagas _dispatchToSagas;
        private readonly IJobScheduler _jobScheduler;
        private readonly IResolver _resolver;
        private readonly IEventFlowConfiguration _eventFlowConfiguration;
        private readonly ICancellationConfiguration _cancellationConfiguration;
        private readonly IReadOnlyCollection<ISubscribeSynchronousToAll> _subscribeSynchronousToAlls;
        private readonly IReadOnlyCollection<IReadStoreManager> _readStoreManagers;
        private readonly IRecoveryHandlerProcessor _recoveryHandlerProcessor;

        public DomainEventPublisher(
            IDispatchToEventSubscribers dispatchToEventSubscribers,
            IDispatchToSagas dispatchToSagas,
            IJobScheduler jobScheduler,
            IResolver resolver,
            IEventFlowConfiguration eventFlowConfiguration,
            IEnumerable<IReadStoreManager> readStoreManagers,
            IEnumerable<ISubscribeSynchronousToAll> subscribeSynchronousToAlls,
            ICancellationConfiguration cancellationConfiguration,
            IRecoveryHandlerProcessor recoveryHandlerProcessor)
        {
            _dispatchToEventSubscribers = dispatchToEventSubscribers;
            _dispatchToSagas = dispatchToSagas;
            _jobScheduler = jobScheduler;
            _resolver = resolver;
            _eventFlowConfiguration = eventFlowConfiguration;
            _cancellationConfiguration = cancellationConfiguration;
            _recoveryHandlerProcessor = recoveryHandlerProcessor;
            _subscribeSynchronousToAlls = subscribeSynchronousToAlls.ToList();
            _readStoreManagers = readStoreManagers.ToList();
        }

        public Task PublishAsync<TAggregate, TIdentity>(
            TIdentity id,
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
        {
            return PublishAsync(
                domainEvents,
                cancellationToken);
        }

        public async Task PublishAsync(
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken)
        {
            cancellationToken = _cancellationConfiguration.Limit(cancellationToken, CancellationBoundary.BeforeUpdatingReadStores);
            await PublishToReadStoresAsync(domainEvents, cancellationToken).ConfigureAwait(false);

            cancellationToken = _cancellationConfiguration.Limit(cancellationToken, CancellationBoundary.BeforeNotifyingSubscribers);
            await PublishToSubscribersOfAllEventsAsync(domainEvents, cancellationToken).ConfigureAwait(false);

            // Update subscriptions AFTER read stores have been updated
            await PublishToSynchronousSubscribersAsync(domainEvents, cancellationToken).ConfigureAwait(false);
            await PublishToAsynchronousSubscribersAsync(domainEvents, cancellationToken).ConfigureAwait(false);

            await PublishToSagasAsync(domainEvents, cancellationToken).ConfigureAwait(false);
        }

        private async Task PublishToReadStoresAsync(
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken)
        {
            var updateReadStoresTasks = _readStoreManagers
                .Select(async rsm =>
                {
                    try
                    {
                        await rsm.UpdateReadStoresAsync(domainEvents, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var handled = await _recoveryHandlerProcessor
                            .RecoverReadModelUpdateErrorAsync(rsm, domainEvents, ex, cancellationToken)
                            .ConfigureAwait(false);

                        if (handled)
                        {
                            return;
                        }

                        throw;
                    }
                });
            await Task.WhenAll(updateReadStoresTasks).ConfigureAwait(false);
        }

        private async Task PublishToSubscribersOfAllEventsAsync(
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken)
        {
            var handle = _subscribeSynchronousToAlls
                .Select(async s =>
                {
                    try
                    {
                        await s.HandleAsync(domainEvents, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var handled = await _recoveryHandlerProcessor.RecoverAllSubscriberErrorAsync(
                            domainEvents,
                            ex,
                            cancellationToken)
                            .ConfigureAwait(false);

                        if (handled)
                        {
                            return;
                        }

                        throw;
                    }
                });
            await Task.WhenAll(handle).ConfigureAwait(false);
        }

        private async Task PublishToSynchronousSubscribersAsync(
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken)
        {
            await _dispatchToEventSubscribers.DispatchToSynchronousSubscribersAsync(domainEvents, cancellationToken).ConfigureAwait(false);
        }

        private async Task PublishToAsynchronousSubscribersAsync(
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken)
        {
            if (_eventFlowConfiguration.IsAsynchronousSubscribersEnabled)
            {
                try
                {
                    await Task.WhenAll(domainEvents.Select(
                                           d => _jobScheduler.ScheduleNowAsync(
                                               DispatchToAsynchronousEventSubscribersJob.Create(d, _resolver),
                                               cancellationToken)))
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var handled = await _recoveryHandlerProcessor.RecoverScheduleSubscriberErrorAsync(
                            domainEvents,
                            ex,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (handled)
                    {
                        return;
                    }

                    throw;
                }
            }
        }

        private async Task PublishToSagasAsync(
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken)
        {
            await _dispatchToSagas.ProcessAsync(domainEvents, cancellationToken).ConfigureAwait(false);
        }
    }
}