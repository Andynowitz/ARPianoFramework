using UnityEngine;

namespace ARPIANO.Scripts.Tracking
{
    // Maps between image (pixel) coordinates and the normalized keyboard unit
    // square. Pure managed math so no OpenCV natives are needed at runtime.
    public class KeyboardHomography
    {
        private readonly double[] homography;
        private readonly double[] inverseHomography;

        public KeyboardHomography(Vector2 topLeft,
                                  Vector2 topRight,
                                  Vector2 bottomRight,
                                  Vector2 bottomLeft)
        {
            homography = Solve(
                topLeft, topRight, bottomRight, bottomLeft,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up);

            inverseHomography = Invert(homography);
        }

        public Vector2 ImageToNormalized(Vector2 imagePoint)
        {
            return Apply(homography, imagePoint);
        }

        public Vector2 NormalizedToImage(Vector2 normalizedPoint)
        {
            return Apply(inverseHomography, normalizedPoint);
        }

        // Kept for API compatibility; there are no native resources to release.
        public void Dispose()
        {
        }

        private static double[] Solve(
            Vector2 s0, Vector2 s1, Vector2 s2, Vector2 s3,
            Vector2 d0, Vector2 d1, Vector2 d2, Vector2 d3)
        {
            Vector2[] source = { s0, s1, s2, s3 };
            Vector2[] destination = { d0, d1, d2, d3 };

            double[,] rows = new double[8, 9];

            for (int i = 0; i < 4; i++)
            {
                double x = source[i].x;
                double y = source[i].y;
                double u = destination[i].x;
                double v = destination[i].y;

                int row = i * 2;
                rows[row, 0] = x;
                rows[row, 1] = y;
                rows[row, 2] = 1.0;
                rows[row, 6] = -u * x;
                rows[row, 7] = -u * y;
                rows[row, 8] = u;

                row++;
                rows[row, 3] = x;
                rows[row, 4] = y;
                rows[row, 5] = 1.0;
                rows[row, 6] = -v * x;
                rows[row, 7] = -v * y;
                rows[row, 8] = v;
            }

            // Gauss-Jordan elimination with partial pivoting.
            for (int column = 0; column < 8; column++)
            {
                int pivot = column;
                for (int row = column + 1; row < 8; row++)
                {
                    if (System.Math.Abs(rows[row, column]) >
                        System.Math.Abs(rows[pivot, column]))
                        pivot = row;
                }

                if (System.Math.Abs(rows[pivot, column]) < 1e-12)
                    return new double[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 };

                if (pivot != column)
                {
                    for (int k = column; k < 9; k++)
                    {
                        double swap = rows[column, k];
                        rows[column, k] = rows[pivot, k];
                        rows[pivot, k] = swap;
                    }
                }

                double divisor = rows[column, column];
                for (int k = column; k < 9; k++)
                    rows[column, k] /= divisor;

                for (int row = 0; row < 8; row++)
                {
                    if (row == column)
                        continue;

                    double factor = rows[row, column];
                    if (factor == 0.0)
                        continue;

                    for (int k = column; k < 9; k++)
                        rows[row, k] -= factor * rows[column, k];
                }
            }

            double[] result = new double[9];
            for (int i = 0; i < 8; i++)
                result[i] = rows[i, 8];
            result[8] = 1.0;
            return result;
        }

        private static double[] Invert(double[] h)
        {
            double[] inverse = new double[9];

            inverse[0] = h[4] * h[8] - h[5] * h[7];
            inverse[1] = h[2] * h[7] - h[1] * h[8];
            inverse[2] = h[1] * h[5] - h[2] * h[4];
            inverse[3] = h[5] * h[6] - h[3] * h[8];
            inverse[4] = h[0] * h[8] - h[2] * h[6];
            inverse[5] = h[2] * h[3] - h[0] * h[5];
            inverse[6] = h[3] * h[7] - h[4] * h[6];
            inverse[7] = h[1] * h[6] - h[0] * h[7];
            inverse[8] = h[0] * h[4] - h[1] * h[3];

            double determinant =
                h[0] * inverse[0] + h[1] * inverse[3] + h[2] * inverse[6];

            if (System.Math.Abs(determinant) < 1e-12)
                return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

            for (int i = 0; i < 9; i++)
                inverse[i] /= determinant;

            return inverse;
        }

        private static Vector2 Apply(double[] h, Vector2 point)
        {
            double w = h[6] * point.x + h[7] * point.y + h[8];

            if (System.Math.Abs(w) < 1e-12)
                return point;

            return new Vector2(
                (float)((h[0] * point.x + h[1] * point.y + h[2]) / w),
                (float)((h[3] * point.x + h[4] * point.y + h[5]) / w));
        }
    }
}
