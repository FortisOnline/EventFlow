// The MIT License (MIT)
//
// Copyright (c) 2015-2018 Rasmus Mikkelsen
// Copyright (c) 2015-2018 eBay Software Foundation
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
using EventFlow.ReadStores;
using EventFlow.Sagas;

namespace EventFlow.PublishRecovery
{
    public sealed class RecoveryHandlerProcessor : IRecoveryHandlerProcessor
    {
        private readonly IReliableMarkProcessor _markProcessor;
        private readonly IResolver _resolver;
        private readonly IReadOnlyCollection<IReadStoreManager> _readStoreManagers;

        public RecoveryHandlerProcessor(
            IResolver resolver,
            IReliableMarkProcessor markProcessor,
            IEnumerable<IReadStoreManager> readStoreManagers)
        {
            _resolver = resolver;
            _markProcessor = markProcessor;
            _readStoreManagers = readStoreManagers.ToList();
        }

        public async Task RecoverAfterUnexpectedShutdownAsync(IReadOnlyList<IDomainEvent> eventsForRecovery, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var readStoreManager in _readStoreManagers)
            {
                await RecoverReadModelUpdateAfterShutdownAsync(readStoreManager, eventsForRecovery, cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _markProcessor.MarkEventsPublishedAsync(eventsForRecovery).ConfigureAwait(false);
        }

        public Task<bool> RecoverReadModelUpdateErrorAsync(
            IReadStoreManager readModelType,
            IReadOnlyCollection<IDomainEvent> eventsForRecovery,
            Exception exception,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(false);
            }

            var recoveryHandlers = _resolver.Resolve<IEnumerable<IReadModelRecoveryHandler>>().ToList();

            var wrapper = new MiddlewareWrapper(recoveryHandlers);

            return wrapper.RecoverFromErrorAsync(readModelType, eventsForRecovery, exception, cancellationToken);
        }

        public Task<bool> RecoverAllSubscriberErrorAsync(IReadOnlyCollection<IDomainEvent> eventsForRecovery, Exception exception,
            CancellationToken cancellationToken)
        {
            // TODO: Implement
            return Task.FromResult(false);
        }

        public Task<bool> RecoverSubscriberErrorAsync(object subscriber, IDomainEvent eventForRecovery, Exception exception,
            CancellationToken cancellationToken)
        {
            // TODO: Implement
            return Task.FromResult(false);
        }

        public Task<bool> RecoverScheduleSubscriberErrorAsync(IReadOnlyCollection<IDomainEvent> eventsForRecovery, Exception exception,
            CancellationToken cancellationToken)
        {
            // TODO: Implement
            return Task.FromResult(false);
        }

        public Task<bool> RecoverSagaErrorAsync(ISagaId eventsForRecovery, SagaDetails exception, IDomainEvent cancellationToken,
            Exception exception1, CancellationToken cancellationToken1)
        {
            // TODO: Implement
            return Task.FromResult(false);
        }

        private Task RecoverReadModelUpdateAfterShutdownAsync(
            IReadStoreManager readModelType,
            IReadOnlyCollection<IDomainEvent> eventsForRecovery,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(false);
            }

            var recoveryHandlers = _resolver.Resolve<IEnumerable<IReadModelRecoveryHandler>>().ToList();

            var wrapper = new MiddlewareWrapper(recoveryHandlers);

            return wrapper.RecoverFromShutdownAsync(readModelType, eventsForRecovery, cancellationToken);
        }

        private sealed class MiddlewareWrapper
        {
            private readonly IReadOnlyList<IReadModelRecoveryHandler> _handlers;
            private readonly int _position;

            public MiddlewareWrapper(IReadOnlyList<IReadModelRecoveryHandler> handlers, int position = 0)
            {
                _handlers = handlers;
                _position = position;
            }

            public Task RecoverFromShutdownAsync(
                IReadStoreManager readStoreManager,
                IReadOnlyCollection<IDomainEvent> domainEvents,
                CancellationToken cancellationToken)
            {
                if (_handlers.Count >= _position)
                {
                    return Task.FromResult(false);
                }

                return _handlers[_position].RecoverFromShutdownAsync(
                    readStoreManager,
                    domainEvents,
                    new MiddlewareWrapper(_handlers, _position + 1).RecoverFromShutdownAsync,
                    cancellationToken);
            }


            public Task<bool> RecoverFromErrorAsync(
                IReadStoreManager readStoreManager,
                IReadOnlyCollection<IDomainEvent> eventsForRecovery,
                Exception exception,
                CancellationToken cancellationToken)
            {
                if (_handlers.Count >= _position)
                {
                    return Task.FromResult(false);
                }

                return _handlers[_position].RecoverFromErrorAsync(
                    readStoreManager,
                    eventsForRecovery,
                    exception,
                    new MiddlewareWrapper(_handlers, _position + 1).RecoverFromErrorAsync,
                    cancellationToken);
            }
        }
    }
}