using System;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Runtime.FullRun;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HollowKnightTAS.Core.Tests.FullRun
{
    [TestClass]
    public sealed class WorldOverviewProjectionTests
    {
        [TestMethod]
        public void LargeHeroFsmKeepsPositionResourcesAndStateInOverview()
        {
            // Same core schema as the live Knight details; large FSM variables alone
            // must not turn its independently useful resources/position into an ID stub.
            var hero = Hero();
            hero["fsms"] = new JArray(new JObject
            {
                ["componentIndex"] = 11, ["name"] = "Spell Control", ["activeState"] = "Inactive",
                ["enabled"] = true, ["variableCount"] = 1,
                ["variables"] = new JArray(new JObject { ["name"] = "Mod text", ["value"] = new string('中', 100000) })
            });
            var cache = new WorldObservationCache();
            string original = hero.ToString(Formatting.None);
            var snapshot = cache.AddSnapshot(1972, 1500, "{}", new[] { Tuple.Create("hero-id", "hero", original) });
            var page = cache.ReadSnapshot(snapshot, 0, 128);
            var reduced = JObject.Parse(page["snapshotJson"])["objects"]![0]!;
            Assert.IsTrue((bool)reduced["detailsRequired"]!);
            Assert.AreEqual("Knight", (string)reduced["name"]!);
            Assert.AreEqual(11.18, (double)reduced["transform"]!["position"]!["x"]!);
            Assert.AreEqual(9, (int)reduced["hero"]!["resources"]!["health"]!);
            Assert.IsTrue((bool)reduced["hero"]!["resources"]!["atBench"]!);
            Assert.AreEqual(55, (int)reduced["hero"]!["cState"]!["fieldCount"]!);
            Assert.AreEqual("Inactive", (string)reduced["fsms"]![0]!["activeState"]!);
            Assert.IsNull(reduced["fsms"]![0]!["variables"]);
            StringAssert.Contains((string)reduced["overviewReduction"]!["fsms"]!["scope"]!, "variables");
            Assert.IsTrue(reduced.ToString(Formatting.None).Length <= 80000);
            Assert.IsTrue(IpcPayloadCodec.Serialize(page).Length < 1024 * 1024);

            // Projection never changes the cached full detail document.
            var detailId = cache.AddDetails("hero-id", 1972, 1500, original);
            int cursor = 0;
            var joined = new StringBuilder();
            while (cursor != -1)
            {
                var chunk = cache.ReadDetails(detailId, "hero-id", 1972, cursor, 100000);
                joined.Append(chunk["detailsJson"]);
                cursor = int.Parse(chunk["nextCursor"]);
            }
            Assert.AreEqual(original, joined.ToString());
        }

        [TestMethod]
        public void LargeColliderPathsKeepBoundsAndRawRadiusWithExplicitOmission()
        {
            var source = Hero();
            source["colliders"] = new JArray(new JObject
            {
                ["componentIndex"] = 13, ["type"] = "UnityEngine.CircleCollider2D", ["enabled"] = true,
                ["bounds"] = new JObject { ["center"] = Vector(11.18, 36.3), ["size"] = Vector(2, 2) },
                ["definition"] = new JObject { ["radius"] = 1.0, ["worldRadius"] = 1.0 },
                ["worldPaths"] = new JArray(new JObject { ["points"] = new string('x', 100000) }),
                ["screenPaths"] = new JArray(new JObject { ["points"] = new string('y', 100000) })
            });
            var cache = new WorldObservationCache();
            var id = cache.AddSnapshot(1, 1, "{}", new[] { Tuple.Create("hero-id", "hero", source.ToString(Formatting.None)) });
            var result = JObject.Parse(cache.ReadSnapshot(id, 0, 1)["snapshotJson"])["objects"]![0]!;
            var collider = result["colliders"]![0]!;
            Assert.AreEqual(1.0, (double)collider["definition"]!["radius"]!);
            Assert.AreEqual(11.18, (double)collider["bounds"]!["center"]!["x"]!);
            Assert.IsTrue((bool)collider["worldPaths"]!["omitted"]!);
            Assert.AreEqual(1, (int)collider["worldPaths"]!["count"]!);
            Assert.IsTrue((bool)collider["screenPaths"]!["detailsRequired"]!);
        }

        [TestMethod]
        public void ExcessCoreFieldsAndCollectionsStayBoundedWithoutLosingHeroResources()
        {
            var source = Hero();
            source["name"] = new string('\0', 30000);
            source["hero"]!["abilities"] = new JObject { ["fields"] = new string('中', 60000) };
            source["health"] = new JArray(new JObject { ["modPayload"] = new string('中', 100000) });
            source["fsms"] = new JArray(Enumerable.Range(0, 500).Select(i => new JObject
            { ["componentIndex"] = i, ["name"] = new string('名', 2000), ["activeState"] = "Wait", ["enabled"] = true }));
            var cache = new WorldObservationCache();
            var id = cache.AddSnapshot(1, 1, "{\"names\":\"" + new string('中', 30000) + "\"}",
                Enumerable.Range(0, 4).Select(i => Tuple.Create("hero-" + i, "hero", source.ToString(Formatting.None))));
            int offset = 0, count = 0;
            while (offset != -1)
            {
                var page = cache.ReadSnapshot(id, offset, 128);
                var root = JObject.Parse(page["snapshotJson"]);
                foreach (var item in (JArray)root["objects"]!)
                {
                    Assert.AreEqual(9, (int)item["hero"]!["resources"]!["health"]!);
                    Assert.IsTrue((bool)item["hero"]!["abilities"]!["omitted"]!);
                    Assert.IsTrue((bool)item["name"]!["omitted"]!);
                    Assert.IsTrue((bool)item["health"]!["omitted"]!);
                    Assert.IsTrue((int)item["overviewReduction"]!["fsms"]!["omittedCount"]! > 0);
                    Assert.IsTrue(item.ToString(Formatting.None).Length <= 80000);
                    count++;
                }
                Assert.IsTrue(IpcPayloadCodec.Serialize(page).Length < 1024 * 1024);
                offset = (int)root["nextOffset"]!;
            }
            Assert.AreEqual(4, count);
        }

        private static JObject Hero() => new JObject
        {
            ["id"] = "hero-id", ["kind"] = "hero", ["name"] = "Knight", ["path"] = "Knight",
            ["scene"] = new JObject { ["handle"] = -12, ["name"] = "DontDestroyOnLoad" },
            ["activeSelf"] = true, ["activeInHierarchy"] = true,
            ["transform"] = new JObject { ["position"] = Vector(11.18, 37.1033249) },
            ["rigidbodies"] = new JArray(new JObject { ["position"] = Vector(11.18, 37.1033249), ["velocity"] = Vector(0, 0) }),
            ["hero"] = new JObject
            {
                ["resources"] = new JObject { ["health"] = 9, ["MPCharge"] = 0, ["atBench"] = true },
                ["charms"] = new JObject { ["equippedCharms"] = new JObject { ["items"] = new JArray(36, 32, 18, 13, 25, 5) } },
                ["cState"] = new JObject { ["fieldCount"] = 55, ["fields"] = new JArray(new JObject { ["name"] = "onGround", ["value"] = false }) }
            }
        };

        private static JObject Vector(double x, double y) => new JObject { ["x"] = x, ["y"] = y };
    }
}
