using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Entity.Enum;
using ACE.Server.Factories;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers.Processors
{
    class DatabasePerfTest
    {
        public const int DefaultBiotasTestCount = 1000;

        private readonly List<uint> testWeenies = new List<uint>
        {
            1,      // Clay
            9035,   // Exarch Plate Girth
            9034,   // Exarch Plate Coat
            27361,  // Palenqual's Ukira of the Vortex
            29947,  // Bracelet of Creature Enchantments
        };

        private static bool SessionIsStillInWorld(Session session)
        {
            return session.State == Network.Enum.SessionState.WorldConnected && session.Player != null;
        }

        public void RunAsync(Session session, int biotasPerTest = DefaultBiotasTestCount)
        {
            Task.Run(() => Run(session, biotasPerTest));
        }

        private void Run(Session session, int biotasPerTest)
        {
            ChatPacket.SendServerMessage(session, $"Starting Shard Database Performance Tests.\nBiotas per test: {biotasPerTest}\nThis may take several minutes to complete...\nCurrent database queue count: {DatabaseManager.Shard.QueueCount}", ChatMessageType.System);


            // Get the current queue wait time
            bool responseReceived = false;

            DatabaseManager.Shard.GetCurrentQueueWaitTime(result =>
            {
                ChatPacket.SendServerMessage(session, $"Current database queue wait time: {result.TotalMilliseconds:N0} ms", ChatMessageType.System);
                responseReceived = true;
            });

            while (!responseReceived)
                Thread.Sleep(1);


            // Generate Individual WorldObjects
            var biotas = new Collection<(Biota biota, ReaderWriterLockSlim rwLock)>();

            for (int i = 0; i < biotasPerTest; i++)
            {
                var worldObject = WorldObjectFactory.CreateNewWorldObject(testWeenies[i % testWeenies.Count]);
                biotas.Add((worldObject.Biota, worldObject.BiotaDatabaseLock));
            }


            // Add biotasPerTest biotas individually
            long trueResults = 0;
            long falseResults = 0;
            var startTime = DateTime.UtcNow;
            var initialQueueWaitTime = TimeSpan.Zero;
            var totalQueryExecutionTime = TimeSpan.Zero;
            foreach (var biota in biotas)
            {
                DatabaseManager.Shard.SaveBiota(biota.biota, biota.rwLock, result =>
                {
                    if (result)
                        Interlocked.Increment(ref trueResults);
                    else
                        Interlocked.Increment(ref falseResults);
                }, (queueWaitTime, queryExecutionTime) =>
                {
                    if (initialQueueWaitTime == TimeSpan.Zero)
                        initialQueueWaitTime = queueWaitTime;

                    totalQueryExecutionTime += queryExecutionTime;
                });
            }

            while (Interlocked.Read(ref trueResults) + Interlocked.Read(ref falseResults) < biotas.Count)
                Thread.Sleep(1);

            var endTime = DateTime.UtcNow;
            ReportResult(session, "individual add", biotasPerTest, (endTime - startTime), initialQueueWaitTime, totalQueryExecutionTime, trueResults, falseResults);


            // Update biotasPerTest biotas individually
            if (SessionIsStillInWorld(session))
            {
                ModifyBiotas(biotas);

                trueResults = 0;
                falseResults = 0;
                startTime = DateTime.UtcNow;
                initialQueueWaitTime = TimeSpan.Zero;
                totalQueryExecutionTime = TimeSpan.Zero;

                foreach (var biota in biotas)
                {
                    DatabaseManager.Shard.SaveBiota(biota.biota, biota.rwLock, result =>
                    {
                        if (result)
                            Interlocked.Increment(ref trueResults);
                        else
                            Interlocked.Increment(ref falseResults);
                    }, (queueWaitTime, queryExecutionTime) =>
                    {
                        if (initialQueueWaitTime == TimeSpan.Zero)
                            initialQueueWaitTime = queueWaitTime;

                        totalQueryExecutionTime += queryExecutionTime;
                    });
                }

                while (Interlocked.Read(ref trueResults) + Interlocked.Read(ref falseResults) < biotas.Count)
                    Thread.Sleep(1);

                endTime = DateTime.UtcNow;
                ReportResult(session, "individual save", biotasPerTest, (endTime - startTime), initialQueueWaitTime, totalQueryExecutionTime, trueResults, falseResults);
            }


            // Delete biotasPerTest biotas individually
            trueResults = 0;
            falseResults = 0;
            startTime = DateTime.UtcNow;
            initialQueueWaitTime = TimeSpan.Zero;
            totalQueryExecutionTime = TimeSpan.Zero;

            foreach (var biota in biotas)
            {
                DatabaseManager.Shard.RemoveBiota(biota.biota, biota.rwLock, result =>
                {
                    if (result)
                        Interlocked.Increment(ref trueResults);
                    else
                        Interlocked.Increment(ref falseResults);
                }, (queueWaitTime, queryExecutionTime) =>
                {
                    if (initialQueueWaitTime == TimeSpan.Zero)
                        initialQueueWaitTime = queueWaitTime;

                    totalQueryExecutionTime += queryExecutionTime;
                });
            }

            while (Interlocked.Read(ref trueResults) + Interlocked.Read(ref falseResults) < biotas.Count)
                Thread.Sleep(1);

            endTime = DateTime.UtcNow;
            ReportResult(session, "individual remove", biotasPerTest, (endTime - startTime), initialQueueWaitTime, totalQueryExecutionTime, trueResults, falseResults);


            if (!SessionIsStillInWorld(session))
                return;

            // Generate Bulk WorldObjects
            biotas.Clear();

            for (int i = 0; i < biotasPerTest; i++)
            {
                var worldObject = WorldObjectFactory.CreateNewWorldObject(testWeenies[i % testWeenies.Count]);
                biotas.Add((worldObject.Biota, worldObject.BiotaDatabaseLock));
            }


            // Add biotasPerTest biotas in bulk
            trueResults = 0;
            falseResults = 0;
            startTime = DateTime.UtcNow;
            initialQueueWaitTime = TimeSpan.Zero;
            totalQueryExecutionTime = TimeSpan.Zero;

            DatabaseManager.Shard.SaveBiotas(biotas, result =>
            {
                if (result)
                    Interlocked.Increment(ref trueResults);
                else
                    Interlocked.Increment(ref falseResults);
            }, (queueWaitTime, queryExecutionTime) =>
            {
                if (initialQueueWaitTime == TimeSpan.Zero)
                    initialQueueWaitTime = queueWaitTime;

                totalQueryExecutionTime += queryExecutionTime;
            });

            while (Interlocked.Read(ref trueResults) + Interlocked.Read(ref falseResults) < 1)
                Thread.Sleep(1);

            endTime = DateTime.UtcNow;
            ReportResult(session, "bulk add", biotasPerTest, (endTime - startTime), initialQueueWaitTime, totalQueryExecutionTime, trueResults, falseResults);


            // Update biotasPerTest biotas in bulk
            if (SessionIsStillInWorld(session))
            {
                ModifyBiotas(biotas);

                trueResults = 0;
                falseResults = 0;
                startTime = DateTime.UtcNow;
                initialQueueWaitTime = TimeSpan.Zero;
                totalQueryExecutionTime = TimeSpan.Zero;

                DatabaseManager.Shard.SaveBiotas(biotas, result =>
                {
                    if (result)
                        Interlocked.Increment(ref trueResults);
                    else
                        Interlocked.Increment(ref falseResults);
                }, (queueWaitTime, queryExecutionTime) =>
                {
                    if (initialQueueWaitTime == TimeSpan.Zero)
                        initialQueueWaitTime = queueWaitTime;

                    totalQueryExecutionTime += queryExecutionTime;
                });

                while (Interlocked.Read(ref trueResults) + Interlocked.Read(ref falseResults) < 1)
                    Thread.Sleep(1);

                endTime = DateTime.UtcNow;
                ReportResult(session, "bulk save", biotasPerTest, (endTime - startTime), initialQueueWaitTime, totalQueryExecutionTime, trueResults, falseResults);
            }


            // Delete biotasPerTest biotas in bulk
            trueResults = 0;
            falseResults = 0;
            startTime = DateTime.UtcNow;
            initialQueueWaitTime = TimeSpan.Zero;
            totalQueryExecutionTime = TimeSpan.Zero;

            DatabaseManager.Shard.RemoveBiotas(biotas, result =>
            {
                if (result)
                    Interlocked.Increment(ref trueResults);
                else
                    Interlocked.Increment(ref falseResults);
            }, (queueWaitTime, queryExecutionTime) =>
            {
                if (initialQueueWaitTime == TimeSpan.Zero)
                    initialQueueWaitTime = queueWaitTime;

                totalQueryExecutionTime += queryExecutionTime;
            });

            while (Interlocked.Read(ref trueResults) + Interlocked.Read(ref falseResults) < 1)
                Thread.Sleep(1);

            endTime = DateTime.UtcNow;
            ReportResult(session, "bulk remove", biotasPerTest, (endTime - startTime), initialQueueWaitTime, totalQueryExecutionTime, trueResults, falseResults);


            if (!SessionIsStillInWorld(session))
                return;

            ChatPacket.SendServerMessage(session, "Database Performance Tests Completed", ChatMessageType.System);
        }

        private static void ModifyBiotas(ICollection<(Biota biota, ReaderWriterLockSlim rwLock)> biotas)
        {
            foreach (var entry in biotas)
            {
                var biota = entry.biota;

                // Change the first record
                if (biota.BiotaPropertiesInt.Count > 0)
                    biota.BiotaPropertiesInt.First().Value++;

                if (biota.BiotaPropertiesInt64.Count > 0)
                    biota.BiotaPropertiesInt64.First().Value++;

                if (biota.BiotaPropertiesIID.Count > 0)
                    biota.BiotaPropertiesIID.First().Value++;

                if (biota.BiotaPropertiesDID.Count > 0)
                    biota.BiotaPropertiesDID.First().Value++;

                if (biota.BiotaPropertiesFloat.Count > 0)
                    biota.BiotaPropertiesFloat.First().Value++;

                if (biota.BiotaPropertiesBool.Count > 0)
                    biota.BiotaPropertiesBool.First().Value = !biota.BiotaPropertiesBool.First().Value;

                if (biota.BiotaPropertiesString.Count > 0)
                    biota.BiotaPropertiesString.First().Value += " test";


                // Remove the last record
                if (biota.BiotaPropertiesInt.Count > 0)
                    biota.BiotaPropertiesInt.Remove(biota.BiotaPropertiesInt.Last());

                if (biota.BiotaPropertiesInt64.Count > 0)
                    biota.BiotaPropertiesInt64.Remove(biota.BiotaPropertiesInt64.Last());

                if (biota.BiotaPropertiesIID.Count > 0)
                    biota.BiotaPropertiesIID.Remove(biota.BiotaPropertiesIID.Last());

                if (biota.BiotaPropertiesDID.Count > 0)
                    biota.BiotaPropertiesDID.Remove(biota.BiotaPropertiesDID.Last());

                if (biota.BiotaPropertiesFloat.Count > 0)
                    biota.BiotaPropertiesFloat.Remove(biota.BiotaPropertiesFloat.Last());

                if (biota.BiotaPropertiesBool.Count > 0)
                    biota.BiotaPropertiesBool.Remove(biota.BiotaPropertiesBool.Last());

                if (biota.BiotaPropertiesString.Count > 0)
                    biota.BiotaPropertiesString.Remove(biota.BiotaPropertiesString.Last());


                // Add a new record
                biota.BiotaPropertiesInt.Add(new BiotaPropertiesInt { ObjectId = biota.Id, Type = ushort.MaxValue, Value = 0, Object = biota });

                biota.BiotaPropertiesInt64.Add(new BiotaPropertiesInt64 { ObjectId = biota.Id, Type = ushort.MaxValue, Value = 0, Object = biota });

                biota.BiotaPropertiesIID.Add(new BiotaPropertiesIID { ObjectId = biota.Id, Type = ushort.MaxValue, Value = 0, Object = biota });

                biota.BiotaPropertiesDID.Add(new BiotaPropertiesDID { ObjectId = biota.Id, Type = ushort.MaxValue, Value = 0, Object = biota });

                biota.BiotaPropertiesFloat.Add(new BiotaPropertiesFloat { ObjectId = biota.Id, Type = ushort.MaxValue, Value = 0, Object = biota });

                biota.BiotaPropertiesBool.Add(new BiotaPropertiesBool { ObjectId = biota.Id, Type = ushort.MaxValue, Value = false, Object = biota });

                biota.BiotaPropertiesString.Add(new BiotaPropertiesString { ObjectId = biota.Id, Type = ushort.MaxValue, Value = "", Object = biota });
            }
        }

        private static void ReportResult(Session session, string testDescription, int biotasPerTest, TimeSpan duration, TimeSpan queueWaitTime, TimeSpan totalQueryExecutionTime, long trueResults, long falseResults)
        {
            if (!SessionIsStillInWorld(session))
                return;

            ChatPacket.SendServerMessage(session, $"{biotasPerTest} {testDescription.PadRight(17)} Duration: {duration.TotalSeconds.ToString("N1").PadLeft(5)} s. Queue Wait Time: {queueWaitTime.TotalMilliseconds.ToString("N0").PadLeft(3)} ms. Average Execution Time: {(totalQueryExecutionTime.TotalMilliseconds / biotasPerTest).ToString("N0").PadLeft(3)} ms. Success/Fail: {trueResults}/{falseResults}.", ChatMessageType.System);
        }
    }
}