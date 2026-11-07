using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // F5u (7): the default key names are real KeyCodes, and the MP host's own input step (MpHost.ReadPlayer) turns held keys
    // into the input the session receives.
    public sealed class F5uKeyTests
    {
        private static readonly Vector3 Mouse = new Vector3(50f, 50f, 0f);

        private static Func<KeyCode, bool> Holding(params KeyCode[] keys)
        {
            var down = new HashSet<KeyCode>(keys);
            return k => down.Contains(k);
        }

        private static PlayerInput Read(bool focused, params KeyCode[] keys)
        {
            Func<KeyCode, bool> source = Holding(keys);
            return MpHost.ReadPlayer(new PlayerInputMapper(PlayerKeys.Default), new PlayerKeyPoller(PlayerKeys.Default, source),
                new IsoCamera(20f, 0f), focused, source, Mouse, 100f, 100f, false);
        }

        [Test]
        public void F5uDefaultKeyNames_AreDistinctKeyCodes()
        {
            var seen = new HashSet<KeyCode>();
            foreach (PlayerKeyBinding binding in PlayerKeys.Default)
            {
                Assert.That(Enum.TryParse(binding.Key, false, out KeyCode code), Is.True, binding.Key);
                Assert.That(seen.Add(code), Is.True, binding.Key);
            }
            Assert.That(seen.Count, Is.EqualTo(7));
        }

        [Test]
        public void F5uReadPlayer_TurnsHeldKeysIntoActions()
        {
            PlayerInput right = Read(true, KeyCode.Mouse1);
            Assert.That((right.Block, right.Throw, right.SkillSlot), Is.EqualTo((true, false, 0)));
            PlayerInput g = Read(true, KeyCode.G);
            Assert.That((g.Block, g.Throw, g.SkillSlot), Is.EqualTo((false, true, 0)));
            PlayerInput two = Read(true, KeyCode.Alpha2);
            Assert.That((two.Block, two.Throw, two.SkillSlot), Is.EqualTo((false, false, 2)));
            PlayerInput d = Read(true, KeyCode.D);
            Assert.That((d.Block, d.Throw, d.SkillSlot), Is.EqualTo((false, false, 0)));
            Assert.That((d.MoveXMilli, d.MoveZMilli), Is.Not.EqualTo((0, 0)));
            PlayerInput unfocused = Read(false, KeyCode.Mouse1, KeyCode.G, KeyCode.Alpha2, KeyCode.D);
            Assert.That((unfocused.Block, unfocused.Throw, unfocused.SkillSlot), Is.EqualTo((false, false, 0)));
            Assert.That((unfocused.MoveXMilli, unfocused.MoveZMilli), Is.EqualTo((0, 0)));
        }

        [Test]
        public void F5uReadPlayer_ReachesTheSessionThroughLiveInput()
        {
            PlayerInput input = Read(true, KeyCode.Mouse1, KeyCode.G, KeyCode.Alpha2);
            Assert.That((input.Block, input.Throw, input.SkillSlot), Is.EqualTo((true, true, 2)));
            SimSession session = SimSession.Create(DemoRecipes.MpBlockDemo(), M2Bake.MatchMapBytes(true));
            new LivePlayerInput().Apply(session, input, false);
            session.StepOnce();
            Assert.That(session.PlayerLog.Count, Is.EqualTo(1));
            PlayerInput got = session.PlayerLog[session.PlayerLog.Count - 1].Input;
            Assert.That((got.Block, got.Throw, got.SkillSlot), Is.EqualTo((true, true, 2)));
        }

        // The host's Update goes through ReadPlayer, and ReadPlayer holds the only call to the mapper.
        [Test]
        public void F5uMpHostSource_UpdateCallsReadPlayerAndMapIsCalledOnce()
        {
            string host = File.ReadAllText(Path.Combine(Application.dataPath, "WarCore/Unity/MpHost.cs"));
            int update = host.IndexOf("private void Update()", StringComparison.Ordinal);
            int show = host.IndexOf("private void Show()", StringComparison.Ordinal);
            Assert.That(update, Is.GreaterThan(0));
            Assert.That(show, Is.GreaterThan(update));
            Assert.That(Count(host.Substring(update, show - update), "ReadPlayer("), Is.EqualTo(1));
            Assert.That(Count(host, ".Map("), Is.EqualTo(1));
            int read = host.IndexOf("public static PlayerInput ReadPlayer(", StringComparison.Ordinal);
            int editorOnly = host.IndexOf("#if UNITY_EDITOR", StringComparison.Ordinal);
            int map = host.IndexOf(".Map(", StringComparison.Ordinal);
            Assert.That(read, Is.GreaterThan(0));
            Assert.That(map, Is.GreaterThan(read));
            Assert.That(editorOnly, Is.GreaterThan(map));
        }

        private static int Count(string text, string part)
        {
            int count = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        [Test]
        public void F5uPoller_RejectsAKeyNameThatIsNotAKeyCode()
        {
            // "8" and "Mouse1, G" would parse with Enum.TryParse (KeyCode 8 is Backspace) but are not key names.
            foreach (string name in new[] { "NoSuchKey", "8", "Mouse1, G" })
            {
                var table = new List<PlayerKeyBinding>(PlayerKeys.Default) { new PlayerKeyBinding(name, PlayerKeyAction.Throw) };
                Assert.Throws<ArgumentException>(() => new PlayerKeyPoller(table, k => false), name);
            }
        }

        [Test]
        public void F5uPolling_AllocatesNothingAfterWarmUp()
        {
            var poller = new PlayerKeyPoller(PlayerKeys.Default, Holding(KeyCode.Space, KeyCode.Alpha4));
            int held = 0;
            for (int i = 0; i < 10; i++) held += poller.KeyDown(true)("Space") ? 1 : 0;
            // Calibration (as AllocationMeter does): a kept array of known size must show up in the counter.
            long before = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long grown = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
            if (grown < 32768) Assert.Fail("The allocation counter did not grow by 32768 bytes: " + grown);
            held = 0;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                Func<string, bool> read = poller.KeyDown(true);
                if (read("Space")) held++;
                if (read("G")) held += 1000;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L));
            Assert.That(held, Is.EqualTo(1000));
        }
    }
}
