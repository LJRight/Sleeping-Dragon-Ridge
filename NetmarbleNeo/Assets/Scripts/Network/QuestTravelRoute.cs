using System.Collections.Generic;
using UnityEngine;

// The centre lines used to shape the authored roads and verify player-capsule clearance.
// Coordinates are relative to each map, before SceneWorld places it on the server.
internal static class QuestTravelRoute
{
    internal static readonly Vector2[] Village = {
        new(0,-14), new(0,-8), new(-.15f,-3), new(0,-.2f), new(1.15f,3),
        new(1.3f,7), new(.5f,13), new(.1f,19), new(0,24), new(0,29)
    };

    internal static readonly Vector2[] Field = SampleField();

    private static Vector2[] SampleField()
    {
        Vector2[] road = {
            new(-154,-134), new(-151,-127), new(-143,-116), new(-133,-115), new(-122,-102), new(-109,-84),
            new(-86,-79), new(-71,-74), new(-59,-77), new(-53,-87), new(-42,-94), new(-11,-92),
            new(12,-96), new(31,-86), new(35,-74), new(49,-69), new(62,-69), new(68,-57), new(95,-28),
            new(97,-3), new(93,10), new(82,19), new(82,29), new(74,39), new(59,43), new(37,71),
            new(11,77), new(-3,89), new(-13,97), new(-24,96), new(-34,88), new(-49,90), new(-77,81),
            new(-101,84), new(-112,91), new(-115,102), new(-129,105), new(-137,115), new(-147,120)
        };
        var result = new List<Vector2> { road[0] };
        for (int i = 1; i < road.Length; i++)
        {
            Vector2 a = road[Mathf.Max(0,i-2)], b = road[i-1], c = road[i], d = road[Mathf.Min(i+1,road.Length-1)];
            int steps = Mathf.CeilToInt(Vector2.Distance(b,c) / 5f);
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                result.Add(.5f * (2*b + (-a+c)*t + (2*a-5*b+4*c-d)*t*t + (-a+3*b-3*c+d)*t*t*t));
            }
        }
        return result.ToArray();
    }

    internal static int Nearest(Vector2[] road, Vector2 position)
    {
        int nearest = 0;
        float shortest = float.PositiveInfinity;
        for (int i = 0; i < road.Length; i++)
        {
            float distance = (road[i] - position).sqrMagnitude;
            if (distance >= shortest) continue;
            shortest = distance;
            nearest = i;
        }
        return nearest;
    }
}
