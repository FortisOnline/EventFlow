using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventFlow.Aggregates;
using EventFlow.Configuration;
using EventFlow.Extensions;
using EventFlow.PublishRecovery;
using EventFlow.ReadStores;
using EventFlow.TestHelpers;
using EventFlow.TestHelpers.Aggregates;
using EventFlow.TestHelpers.Aggregates.Events;
using FluentAssertions;
using NUnit.Framework;

namespace EventFlow.Tests.IntegrationTests.ReadStores
{
    public sealed class ReadModelRecoveryTests : IntegrationTest
    {
        protected override IRootResolver CreateRootResolver(IEventFlowOptions eventFlowOptions)
        {
            return eventFlowOptions
                .UseInMemoryReadStoreFor<FailingReadModel>()
                .UseReadModelRecoveryHandler<FailingReadModel, TestRecoveryHandler>(Lifetime.Singleton)
                .CreateResolver();
        }

        [Test]
        public async Task ShouldRecoveryForExceptionInReadModel()
        {
            var recoveryHandler = (TestRecoveryHandler)Resolver.Resolve<IReadModelRecoveryHandler<FailingReadModel>>();
            recoveryHandler.ShouldRecover = true;

            await PublishPingCommandAsync(ThingyId.New);

            recoveryHandler.LastRecoveredEvents.Should()
                .ContainSingle(x => x.GetAggregateEvent() is ThingyPingEvent);
        }

        [Test]
        public async Task ShouldThrowOriginalErrorWhenNoRecovery()
        {
            var recoveryHandler = (TestRecoveryHandler)Resolver.Resolve<IReadModelRecoveryHandler<FailingReadModel>>();
            recoveryHandler.ShouldRecover = false;

            Func<Task> publishPing = () => PublishPingCommandAsync(ThingyId.New);

            (await publishPing.Should().ThrowAsync<Exception>().ConfigureAwait(false))
                .WithMessage("Read model exception. Should be recovered.");
        }

        private sealed class FailingReadModel : IReadModel,
            IAmReadModelFor<ThingyAggregate, ThingyId, ThingyPingEvent>
        {
            public void Apply(IReadModelContext context, IDomainEvent<ThingyAggregate, ThingyId, ThingyPingEvent> domainEvent)
            {
                throw new Exception("Read model exception. Should be recovered.");
            }
        }

        private sealed class TestRecoveryHandler : IReadModelRecoveryHandler<FailingReadModel>
        {
            public IReadOnlyCollection<IDomainEvent> LastRecoveredEvents { get; private set; }

            public bool ShouldRecover { get; set; }

            public Task RecoverFromShutdownAsync(IReadOnlyCollection<IDomainEvent> eventsForRecovery, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            public Task<bool> RecoverFromErrorAsync(IReadOnlyCollection<IDomainEvent> eventsForRecovery, Exception exception,
                CancellationToken cancellationToken)
            {
                LastRecoveredEvents = eventsForRecovery;

                return Task.FromResult(ShouldRecover);
            }
        }
    }
}