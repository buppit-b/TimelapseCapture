using System;
using System.Drawing;

namespace TimelapseCapture
{
    /// <summary>
    /// Represents an aspect ratio for capture region constraints.
    /// Provides common presets and utilities for ratio calculations.
    /// </summary>
    public class AspectRatio
    {
        /// <summary>
        /// Display name of the aspect ratio.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Width component of the ratio.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Height component of the ratio.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Calculated decimal ratio (width/height).
        /// </summary>
        public double Ratio => Height > 0 ? (double)Width / Height : 0;

        /// <summary>
        /// Common aspect ratio presets.
        /// </summary>
        public static readonly AspectRatio[] CommonRatios = new[]
        {
            new AspectRatio { Name = "Free (No Constraint)", Width = 0, Height = 0 },
            new AspectRatio { Name = "16:9 (HD/4K)", Width = 16, Height = 9 },
            new AspectRatio { Name = "4:3 (Standard)", Width = 4, Height = 3 },
            new AspectRatio { Name = "1:1 (Square)", Width = 1, Height = 1 },
            new AspectRatio { Name = "21:9 (Ultrawide)", Width = 21, Height = 9 },
            new AspectRatio { Name = "9:16 (Portrait)", Width = 9, Height = 16 },
            new AspectRatio { Name = "2.39:1 (Cinema)", Width = 239, Height = 100 },
            new AspectRatio { Name = "3:2 (Photo)", Width = 3, Height = 2 },
            new AspectRatio { Name = "5:4 (Classic)", Width = 5, Height = 4 },
        };

        /// <summary>
        /// Constrain a rectangle to match this aspect ratio.
        /// Maintains the rectangle's area as closely as possible while adjusting dimensions.
        /// </summary>
        /// <param name="rect">Original rectangle</param>
        /// <param name="ratioW">Target aspect ratio width component</param>
        /// <param name="ratioH">Target aspect ratio height component</param>
        /// <returns>Rectangle constrained to the aspect ratio with even dimensions</returns>
        public static Rectangle ConstrainToRatio(Rectangle rect, int ratioW, int ratioH)
        {
            // Free mode - no constraint
            if (ratioW == 0 || ratioH == 0)
                return EnsureEvenDimensions(rect);

            double targetRatio = (double)ratioW / ratioH;
            double currentRatio = (double)rect.Width / rect.Height;

            int newWidth, newHeight;

            if (currentRatio > targetRatio)
            {
                // Rectangle is too wide - constrain width based on height
                newHeight = rect.Height;
                newWidth = (int)(newHeight * targetRatio);
            }
            else
            {
                // Rectangle is too tall - constrain height based on width
                newWidth = rect.Width;
                newHeight = (int)(newWidth / targetRatio);
            }

            // Ensure even dimensions for video encoding
            newWidth = MakeEven(newWidth);
            newHeight = MakeEven(newHeight);

            // Ensure minimum size
            newWidth = Math.Max(2, newWidth);
            newHeight = Math.Max(2, newHeight);

            return new Rectangle(rect.X, rect.Y, newWidth, newHeight);
        }

        /// <summary>
        /// Ensure both width and height are even numbers (required for video encoding).
        /// </summary>
        public static Rectangle EnsureEvenDimensions(Rectangle rect)
        {
            int width = MakeEven(rect.Width);
            int height = MakeEven(rect.Height);

            return new Rectangle(
                rect.X,
                rect.Y,
                Math.Max(2, width),
                Math.Max(2, height)
            );
        }

        /// <summary>
        /// Make a number even by rounding down if odd.
        /// </summary>
        private static int MakeEven(int value)
        {
            return (value & 1) == 1 ? value - 1 : value;
        }

        /// <summary>
        /// Calculate actual aspect ratio from dimensions.
        /// </summary>
        public static string CalculateRatioString(int width, int height)
        {
            if (height == 0) return "Invalid";

            int gcd = CalculateGCD(width, height);
            int ratioW = width / gcd;
            int ratioH = height / gcd;

            // Check if it matches a common ratio
            foreach (var ratio in CommonRatios)
            {
                if (ratio.Width == ratioW && ratio.Height == ratioH)
                    return ratio.Name;
            }

            return $"{ratioW}:{ratioH}";
        }

        /// <summary>
        /// Calculate Greatest Common Divisor using Euclidean algorithm.
        /// </summary>
        private static int CalculateGCD(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        public override string ToString() => Name;
    }
}