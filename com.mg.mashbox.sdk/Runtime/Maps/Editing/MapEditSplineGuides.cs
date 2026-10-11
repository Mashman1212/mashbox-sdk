using System.Collections.Generic;
using MashBoxSDK.Maps.Spline;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Editing
{
    /// <summary>The same bounded authoring selection drives drawing and knot picking, including inactive rails.</summary>
    public sealed class MapEditSplineGuides
    {
        public const int MaxContainers = 64, MaxKnots = 4096;
        public readonly struct Knot
        {
            public readonly SplineContainer container;
            public readonly int spline, index;
            public Knot(SplineContainer container, int spline, int index)
            { this.container = container; this.spline = spline; this.index = index; }
            public Vector3 WorldPosition => container.transform.TransformPoint((Vector3)container.Splines[spline][index].Position);
        }
        readonly MapEditSession session;
        readonly List<SplineContainer> containers = new List<SplineContainer>();
        public IReadOnlyList<SplineContainer> Containers => containers;
        public MapEditSplineGuides(MapEditSession session) { this.session = session; }
        public static T FindTarget<T>(MapEditSession session, Transform selection) where T : Component
        {
            if (!selection) return null;
            for (var target = selection; target; target = target.parent)
                if (session.Visible(target) && target.TryGetComponent(out T component)) return component;
            foreach (var component in selection.GetComponentsInChildren<T>(true))
                if (session.Visible(component.transform)) return component;
            return null;
        }
        public void Refresh(Transform selection, MultiSplineLoft relatedLoft, IEnumerable<SplineContainer> extra)
        {
            containers.Clear();
            Add(FindTarget<SplineContainer>(session, selection));
            var loft = FindTarget<MultiSplineLoft>(session, selection);
            AddLoft(loft);
            if (relatedLoft != loft) AddLoft(relatedLoft);
            if (extra != null) foreach (var container in extra) Add(container);
        }
        void AddLoft(MultiSplineLoft loft)
        {
            if (!loft || !session.Visible(loft.transform)) return;
            foreach (var source in loft.Sources) if (source?.container) Add(source.container);
        }
        void Add(SplineContainer container)
        {
            if (container && session.Visible(container.transform) && containers.Count < MaxContainers && !containers.Contains(container))
                containers.Add(container);
        }
        public IEnumerable<Knot> Knots()
        {
            int count = 0;
            foreach (var container in containers)
            {
                if (!container || !session.Visible(container.transform)) continue;
                for (int s = 0; s < container.Splines.Count; s++)
                    for (int k = 0; k < container.Splines[s].Count; k++)
                    {
                        if (count++ >= MaxKnots) yield break;
                        yield return new Knot(container, s, k);
                    }
            }
        }
        public bool Pick(Camera camera, Vector2 size, Vector2 pointer, out Knot knot, float radius = 16)
        {
            knot = default; bool found = false; float best = radius * radius;
            foreach (var candidate in Knots())
            {
                var p = camera.WorldToViewportPoint(candidate.WorldPosition);
                if (p.z <= camera.nearClipPlane) continue;
                var point = new Vector2(p.x * size.x, (1 - p.y) * size.y);
                float distance = (point - pointer).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; knot = candidate; found = true;
            }
            return found;
        }
    }
}
