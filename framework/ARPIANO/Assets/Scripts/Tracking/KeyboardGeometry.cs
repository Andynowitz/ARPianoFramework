using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OpenCvSharp;

namespace ARPIANO.Scripts.Tracking
{
    public sealed class KeyboardGeometry
    {
        public Rect MeasurementRegion { get; }
        public IReadOnlyList<float> RawVerticalCandidates { get; }
        public IReadOnlyList<float> Boundaries { get; }
        public IReadOnlyList<float> Widths { get; }
        public IReadOnlyList<float> Centers { get; }
        public float? NearestC3Center { get; }
        public float? NearestC4Center { get; }
        public float? NearestC5Center { get; }

        public bool IsValid
        {
            get
            {
                if (MeasurementRegion.Width <= 0 ||
                    MeasurementRegion.Height <= 0 ||
                    Boundaries.Count < 2 ||
                    Widths.Count != Boundaries.Count - 1 ||
                    Centers.Count != Boundaries.Count - 1)
                    return false;

                for (int i = 0; i < Boundaries.Count; i++)
                {
                    if (!IsFinite(Boundaries[i]) ||
                        (i > 0 && Boundaries[i] <= Boundaries[i - 1]))
                        return false;
                }

                for (int i = 0; i < Widths.Count; i++)
                {
                    if (!IsFinite(Widths[i]) || Widths[i] <= 0f || !IsFinite(Centers[i]))
                        return false;
                }

                return true;
            }
        }

        public KeyboardGeometry(
            Rect measurementRegion,
            IList<float> rawVerticalCandidates,
            IList<float> boundaries,
            IList<float> widths,
            IList<float> centers,
            float? nearestC3Center,
            float? nearestC4Center,
            float? nearestC5Center)
        {
            MeasurementRegion = measurementRegion;
            RawVerticalCandidates = new ReadOnlyCollection<float>(
                new List<float>(rawVerticalCandidates ?? new List<float>()));
            Boundaries = new ReadOnlyCollection<float>(
                new List<float>(boundaries ?? new List<float>()));
            Widths = new ReadOnlyCollection<float>(
                new List<float>(widths ?? new List<float>()));
            Centers = new ReadOnlyCollection<float>(
                new List<float>(centers ?? new List<float>()));
            NearestC3Center = nearestC3Center;
            NearestC4Center = nearestC4Center;
            NearestC5Center = nearestC5Center;
        }

        private bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
