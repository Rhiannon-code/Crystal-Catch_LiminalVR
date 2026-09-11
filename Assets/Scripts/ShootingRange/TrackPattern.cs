using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public enum TrackPatternKind
    {
        Single,       // One target, anywhere
        Gauntlet,     // A train down one rail
        Sweep,        // Every rail of one run, staggered
        RoofRush,     // The whole roof at once
        RiserVolley,  // Both walls climbing
        Crossfire,    // The floor grid from both axes together
        Cascade,      // Floor, then wall, then roof, a wave that climbs the room
    }

    public static class TrackPattern
    {
        public struct Launch
        {
            public TrackRail Rail;
            public float Delay;
            public float SpeedMultiplier;
        }

        private static readonly List<TrackRail> Buffer = new List<TrackRail>();

        public static void Build(TrackPatternKind kind, TrackRail[] rails, List<Launch> into)
        {
            into.Clear();
            if (rails == null || rails.Length == 0) return;

            switch (kind)
            {
                case TrackPatternKind.Gauntlet: Gauntlet(rails, into); break;
                case TrackPatternKind.Sweep: Sweep(rails, into); break;
                case TrackPatternKind.RoofRush: RoofRush(rails, into); break;
                case TrackPatternKind.RiserVolley: Volley(rails, into); break;
                case TrackPatternKind.Crossfire: Crossfire(rails, into); break;
                case TrackPatternKind.Cascade: Cascade(rails, into); break;
                default: Single(rails, into); break;
            }
        }

        private static void Single(TrackRail[] rails, List<Launch> into)
        {
            Add(into, rails[Random.Range(0, rails.Length)], 0f, 1f);
        }

        /// Same speed on purpose, a train only holds its spacing if nothing in it can catch anything
        /// else, and the rail would refuse the bookings otherwise
        private static void Gauntlet(TrackRail[] rails, List<Launch> into)
        {
            var rail = rails[Random.Range(0, rails.Length)];
            for (int i = 0; i < 3; i++) Add(into, rail, i * 0.9f, 1f);
        }

        private static void Sweep(TrackRail[] rails, List<Launch> into)
        {
            var group = Random.value < 0.5f ? TrackGroup.FloorAcross : TrackGroup.RoofAcross;
            Collect(rails, group);

            for (int i = 0; i < Buffer.Count; i++) Add(into, Buffer[i], i * 0.35f, 1f);
        }

        private static void RoofRush(TrackRail[] rails, List<Launch> into)
        {
            Collect(rails, TrackGroup.RoofAcross);
            for (int i = 0; i < Buffer.Count; i++) Add(into, Buffer[i], i * 0.18f, 1.1f);

            Collect(rails, TrackGroup.RoofAlong);
            for (int i = 0; i < Buffer.Count; i++) Add(into, Buffer[i], 0.5f + i * 0.18f, 1.1f);
        }

        private static void Volley(TrackRail[] rails, List<Launch> into)
        {
            Collect(rails, TrackGroup.Riser);
            for (int i = 0; i < Buffer.Count; i++) Add(into, Buffer[i], i * 0.22f, 1f);
        }

        /// Both axes of the floor grid at once. Every one of these crosses three junctions, so this
        /// is the pattern the booking system exists for, it reads as near misses, not collisions
        private static void Crossfire(TrackRail[] rails, List<Launch> into)
        {
            Collect(rails, TrackGroup.FloorAcross);
            for (int i = 0; i < Buffer.Count; i++) Add(into, Buffer[i], i * 0.12f, 1f);

            Collect(rails, TrackGroup.FloorAlong);
            for (int i = 0; i < Buffer.Count; i++) Add(into, Buffer[i], 0.25f + i * 0.12f, 1f);
        }

        private static void Cascade(TrackRail[] rails, List<Launch> into)
        {
            for (int wave = 0; wave < 3; wave++)
            {
                float t = wave * 0.7f;
                AddOneFrom(rails, TrackGroup.FloorAcross, into, t, 1f);
                AddOneFrom(rails, TrackGroup.Riser, into, t + 0.3f, 1f);
                AddOneFrom(rails, TrackGroup.RoofAcross, into, t + 0.6f, 1.05f);
            }
        }

        private static void AddOneFrom(TrackRail[] rails, TrackGroup group, List<Launch> into,
                                       float delay, float speed)
        {
            Collect(rails, group);
            if (Buffer.Count == 0) return;

            Add(into, Buffer[Random.Range(0, Buffer.Count)], delay, speed);
        }

        private static void Add(List<Launch> into, TrackRail rail, float delay, float speed)
        {
            if (rail == null) return;
            into.Add(new Launch { Rail = rail, Delay = delay, SpeedMultiplier = speed });
        }

        private static void Collect(TrackRail[] rails, TrackGroup group)
        {
            Buffer.Clear();
            for (int i = 0; i < rails.Length; i++)
            {
                if (rails[i] != null && rails[i].Group == group) Buffer.Add(rails[i]);
            }
        }
    }
}
