﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Threading.Tests.Sql
{
    public abstract class SqlDistributedSemaphoreTestCases<TConnectionManagementProvider> : TestBase
        where TConnectionManagementProvider : TestingSqlConnectionManagementProvider, new()
    {
        private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(5);

        [TestMethod]
        public void TestConcurrencyHandling()
        {
            using (var engine = this.CreateEngine())
            {
                const int MaxCount = 3;

                var counter = 0;
                var seenCounterValues = new HashSet<int>();

                const int Threads = 10;
                const int Trials = 25;
                using (var barrier = new Barrier(Threads))
                {
                    var threads = Enumerable.Range(0, Threads)
                        .Select(_ => new Thread(() =>
                        {
                            var semaphore = engine.CreateSemaphore(nameof(TestConcurrencyHandling), MaxCount);

                            barrier.SignalAndWait();
                            for (var i = 0; i < Trials; ++i)
                            {
                                using (semaphore.Acquire(LongTimeout))
                                {
                                    var newCounterValue = Interlocked.Increment(ref counter);
                                    lock (seenCounterValues) { seenCounterValues.Add(newCounterValue); }
                                    Thread.Sleep(10);
                                    Interlocked.Decrement(ref counter);
                                }
                            }
                        }))
                        .ToList();
                    threads.ForEach(t => t.Start());
                    threads.ForEach(t => t.Join());
                }

                CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, seenCounterValues.ToArray());
            }
        }

        [TestMethod]
        public void TestDrain()
        {
            using (var engine = this.CreateEngine())
            {
                var semaphore = engine.CreateSemaphore(nameof(TestDrain), maxCount: 4);
                var semaphore2 = engine.CreateSemaphore(nameof(TestDrain), maxCount: 4);

                var handles = new List<IDisposable> { semaphore.Acquire(LongTimeout) };
                TestHelper.AssertDoesNotThrow(() => semaphore2.Acquire().Dispose());
                while (handles.Count < 4) { handles.Add(semaphore.Acquire(LongTimeout)); }

                semaphore2.TryAcquire().ShouldEqual(null);
                semaphore.TryAcquire().ShouldEqual(null);

                handles[0].Dispose();
                TestHelper.AssertDoesNotThrow(() => semaphore2.Acquire().Dispose());

                handles.ForEach(h => h.Dispose());
            }
        }

        [TestMethod]
        public void TestHighTicketCount()
        {
            using (var engine = this.CreateEngine())
            {
                var semaphore = engine.CreateSemaphore($"s{new string('o', 1000)} many tickets!", int.MaxValue);
                var handles = Enumerable.Range(0, 100)
                    .Select(_ => semaphore.Acquire(LongTimeout))
                    .ToList();
                handles.ForEach(h => h.Dispose());
            }
        }

        [TestMethod]
        public void TestSameNameDifferentCounts()
        {
            using (var engine = this.CreateEngine())
            {
                // if 2 semaphores have different views of what the max count is, things still kind of
                // work. The semaphore with the higher count behaves normally. The semaphore with the lower
                // count behaves normally when the number of contenders is below it's count. After that, it
                // behaves unpredictably. For example, if we have counts 2 and 3 and the 3-semaphore holds 2 tickets,
                // then the 2-semaphore might or might not be able to acquire a ticket depending on whether the
                // 3-semaphore holds tickets 1&2 (no), 1&3 (yes), or 2&3 (yes). This test serves to document
                // the behavior that is more well-defined

                var semaphore2 = engine.CreateSemaphore(nameof(TestSameNameDifferentCounts), 2);
                var semaphore3 = engine.CreateSemaphore(nameof(TestSameNameDifferentCounts), 3);

                var handle1 = semaphore2.Acquire(LongTimeout);
                var handle2 = semaphore3.Acquire(LongTimeout);
                var handle3 = semaphore3.Acquire(LongTimeout);
                semaphore2.TryAcquire().ShouldEqual(null);
                semaphore3.TryAcquire().ShouldEqual(null);

                handle1.Dispose();
                handle1 = semaphore3.Acquire(LongTimeout);

                handle1.Dispose();
                handle2.Dispose();
                handle3.Dispose();
            }
        }

        private TestingSqlDistributedSemaphoreEngine<TConnectionManagementProvider> CreateEngine() => new TestingSqlDistributedSemaphoreEngine<TConnectionManagementProvider>();
    }
}
