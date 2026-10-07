using System;
using System.Collections.Generic;
using OpenCvSharp;
using UnityEngine;
using CvRect = OpenCvSharp.Rect;

namespace ARPIANO.Scripts.Tracking
{
    public sealed class KeyboardGeometryDetector
    {
        private readonly int measurementYOffsetPixels;
        private readonly int measurementBandHeightPixels;

        public KeyboardGeometryDetector(int measurementYOffsetPixels, int measurementBandHeightPixels)
        {
            this.measurementYOffsetPixels = measurementYOffsetPixels;
            this.measurementBandHeightPixels = measurementBandHeightPixels;
        }

        public KeyboardGeometry Detect(
            Mat src,
            CvRect keyboardRegion,
            IList<Vector2> markerCenters)
        {
            if (src == null || src.Empty() || markerCenters == null || markerCenters.Count != 3)
                return EmptyGeometry();

            int regionX = Mathf.Clamp(keyboardRegion.X, 0, Mathf.Max(0, src.Width - 1));
            int regionRight = Mathf.Clamp(keyboardRegion.Right, regionX + 1, src.Width);
            int regionTop = Mathf.Clamp(keyboardRegion.Y, 0, Mathf.Max(0, src.Height - 1));
            int markerY = Mathf.RoundToInt(
                (markerCenters[0].y + markerCenters[1].y + markerCenters[2].y) / 3f);
            int measurementY = Mathf.Clamp(
                markerY + measurementYOffsetPixels,
                regionTop,
                Mathf.Max(regionTop, markerY - measurementBandHeightPixels));
            int measurementBottom = Mathf.Min(
                src.Height,
                measurementY + measurementBandHeightPixels);
            if (measurementBottom <= measurementY)
                return EmptyGeometry();

            var measurementRegion = new CvRect(
                regionX,
                measurementY,
                regionRight - regionX,
                measurementBottom - measurementY);
            List<float> rawVerticalCandidates = DetectRawVerticalCandidates(src, measurementRegion);
            List<float> boundaries = DeduplicateCandidates(rawVerticalCandidates);
            var widths = new List<float>();
            var centers = new List<float>();
            for (int i = 0; i + 1 < boundaries.Count; i++)
            {
                widths.Add(boundaries[i + 1] - boundaries[i]);
                centers.Add((boundaries[i] + boundaries[i + 1]) * 0.5f);
            }

            return new KeyboardGeometry(
                measurementRegion,
                rawVerticalCandidates,
                boundaries,
                widths,
                centers,
                FindNearestCenter(markerCenters[0].x, centers),
                FindNearestCenter(markerCenters[1].x, centers),
                FindNearestCenter(markerCenters[2].x, centers));
        }

        private List<float> DeduplicateCandidates(IList<float> candidates)
        {
            var boundaries = new List<float>();
            foreach (float candidate in candidates)
            {
                if (boundaries.Count == 0 ||
                    Mathf.Abs(candidate - boundaries[boundaries.Count - 1]) > 5f)
                    boundaries.Add(candidate);
                else
                    boundaries[boundaries.Count - 1] =
                        (boundaries[boundaries.Count - 1] + candidate) * 0.5f;
            }

            return boundaries;
        }

        private List<float> DetectRawVerticalCandidates(
            Mat src,
            CvRect measurementRegion)
        {
            var candidates = new List<float>();
            using var roi = new Mat(src, measurementRegion);
            using var gray = new Mat();
            using var blurred = new Mat();
            using var edges = new Mat();
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);
            Cv2.Canny(blurred, edges, 25, 90);

            LineSegmentPoint[] lines = Cv2.HoughLinesP(
                edges,
                1,
                Math.PI / 180.0,
                16,
                Math.Max(12, measurementRegion.Height * 0.55),
                8);
            if (lines == null || lines.Length == 0)
                return candidates;

            foreach (LineSegmentPoint line in lines)
            {
                float dx = Math.Abs(line.P1.X - line.P2.X);
                float dy = Math.Abs(line.P1.Y - line.P2.Y);
                if (dy < measurementRegion.Height * 0.55f || dx > dy * 0.2f)
                    continue;

                candidates.Add(
                    measurementRegion.X + (line.P1.X + line.P2.X) * 0.5f);
            }

            candidates.Sort();
            return candidates;
        }

        private float? FindNearestCenter(float markerX, IList<float> centers)
        {
            if (centers == null || centers.Count == 0)
                return null;

            float nearest = centers[0];
            float distance = Mathf.Abs(markerX - nearest);
            for (int i = 1; i < centers.Count; i++)
            {
                float candidateDistance = Mathf.Abs(markerX - centers[i]);
                if (candidateDistance < distance)
                {
                    nearest = centers[i];
                    distance = candidateDistance;
                }
            }

            return nearest;
        }

        private KeyboardGeometry EmptyGeometry()
        {
            return new KeyboardGeometry(
                new CvRect(),
                new List<float>(),
                new List<float>(),
                new List<float>(),
                new List<float>(),
                null,
                null,
                null);
        }
    }
}
