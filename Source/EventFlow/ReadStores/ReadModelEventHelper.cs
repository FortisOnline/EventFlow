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
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EventFlow.Aggregates;
using EventFlow.Extensions;

namespace EventFlow.ReadStores
{
    internal static class ReadModelEventHelper<TReadModel>
        where TReadModel : class, IReadModel
    {
        // ReSharper disable StaticMemberInGenericType
        private static readonly ISet<Type> AggregateEventTypes;
        // ReSharper enable StaticMemberInGenericType

        static ReadModelEventHelper()
        {
            var readModelType = typeof(TReadModel);

            var iAmReadModelForInterfaceTypes = readModelType
                .GetTypeInfo()
                .GetInterfaces()
                .Where(IsReadModelFor)
                .ToList();
            if (!iAmReadModelForInterfaceTypes.Any())
            {
                throw new ArgumentException(
                    $"Read model type '{readModelType.PrettyPrint()}' does not implement any '{typeof(IAmReadModelFor<,,>).PrettyPrint()}'");
            }

            AggregateEventTypes = new HashSet<Type>(iAmReadModelForInterfaceTypes.Select(i => i.GetTypeInfo().GetGenericArguments()[2]));
            if (AggregateEventTypes.Count != iAmReadModelForInterfaceTypes.Count)
            {
                throw new ArgumentException(
                    $"Read model type '{readModelType.PrettyPrint()}' implements ambiguous '{typeof(IAmReadModelFor<,,>).PrettyPrint()}' interfaces");
            }
        }

        private static bool IsReadModelFor(Type i)
        {
            if (!i.GetTypeInfo().IsGenericType)
            {
                return false;
            }

            var typeDefinition = i.GetGenericTypeDefinition();
            return typeDefinition == typeof(IAmReadModelFor<,,>) ||
                   typeDefinition == typeof(IAmAsyncReadModelFor<,,>);
        }

        public static bool CanApply(IDomainEvent domainEvent)
        {
            return CanApply(domainEvent.EventType);
        }

        public static bool CanApply(Type domainEventType)
        {
            return AggregateEventTypes.Contains(domainEventType);
        }

        // Dummy mehtod, all initialization performed in static constructor
        public static void Nop()
        {
        }
    }
}