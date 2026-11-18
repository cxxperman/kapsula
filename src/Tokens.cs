// Generated from tokens/tokens.json (v0.2.0) by tools/build.js. Do not edit by hand.
using System.Windows.Media;

namespace DevTxt
{
    static class Tokens
    {
        public const string Version = "0.2.0";

        public static class Palette
        {
            public static readonly Color Brand = Color.FromRgb(0xF0, 0x8A, 0x4B);
            public static readonly Color Blue = Color.FromRgb(0x0A, 0x84, 0xFF);
            public static readonly Color Green = Color.FromRgb(0x6C, 0xCB, 0x5F);
            public static readonly Color Red = Color.FromRgb(0xC4, 0x2B, 0x1C);
            public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
            public static readonly Color Yellow = Color.FromRgb(0xFC, 0xE1, 0x00);
            public static readonly Color Purple = Color.FromRgb(0x8A, 0x5C, 0xF6);
            public static readonly Color Pink = Color.FromRgb(0xFF, 0x37, 0x5F);
            public static readonly Color Accent = Blue;
        }

        public static class Dark
        {
            public static readonly Color Bg = Color.FromRgb(0x20, 0x20, 0x20);
            public static readonly Color Surface = Color.FromRgb(0x27, 0x27, 0x27);
            public static readonly Color Raised = Color.FromRgb(0x2C, 0x2C, 0x2C);
            public static readonly Color Label = Color.FromRgb(0xFF, 0xFF, 0xFF);
            public static readonly Color Label2 = Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF);
            public static readonly Color Label3 = Color.FromArgb(0x8B, 0xFF, 0xFF, 0xFF);
            public static readonly Color Separator = Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF);
            public static readonly Color Fill = Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF);
            public static readonly Color Fill2 = Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF);
            public static readonly Color FillPress = Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF);
            public static readonly Color Edge = Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF);
            public static readonly Color EdgeTop = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);
            public static readonly Color Menu = Color.FromArgb(0xF5, 0x2C, 0x2C, 0x2C);
            public static readonly Color Tooltip = Color.FromRgb(0x2C, 0x2C, 0x2C);
        }

        public static class GlassDark
        {
            public const double FillTop = 0.0605;
            public const double FillBottom = 0.0605;
            public const double HoverTop = 0.0837;
            public const double HoverBottom = 0.0837;
            public const double StrongTop = 0.0512;
            public const double StrongBottom = 0.0512;
            public static readonly Color[] RimColors = { Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF) };
            public static readonly double[] RimOffsets = { 0, 1 };
        }

        public static class Light
        {
            public static readonly Color Bg = Color.FromRgb(0xF3, 0xF3, 0xF3);
            public static readonly Color Surface = Color.FromRgb(0xF9, 0xF9, 0xF9);
            public static readonly Color Raised = Color.FromRgb(0xFF, 0xFF, 0xFF);
            public static readonly Color Label = Color.FromArgb(0xE4, 0x00, 0x00, 0x00);
            public static readonly Color Label2 = Color.FromArgb(0x9E, 0x00, 0x00, 0x00);
            public static readonly Color Label3 = Color.FromArgb(0x72, 0x00, 0x00, 0x00);
            public static readonly Color Separator = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
            public static readonly Color Fill = Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
            public static readonly Color Fill2 = Color.FromArgb(0x80, 0xF9, 0xF9, 0xF9);
            public static readonly Color FillPress = Color.FromArgb(0x4D, 0xF9, 0xF9, 0xF9);
            public static readonly Color Edge = Color.FromArgb(0x0F, 0x00, 0x00, 0x00);
            public static readonly Color EdgeTop = Color.FromArgb(0x0F, 0x00, 0x00, 0x00);
            public static readonly Color Menu = Color.FromArgb(0xF5, 0xF9, 0xF9, 0xF9);
            public static readonly Color Tooltip = Color.FromRgb(0xF9, 0xF9, 0xF9);
        }

        public static class GlassLight
        {
            public const double FillTop = 0.7;
            public const double FillBottom = 0.7;
            public const double HoverTop = 0.5;
            public const double HoverBottom = 0.5;
            public const double StrongTop = 1;
            public const double StrongBottom = 1;
            public static readonly Color[] RimColors = { Color.FromArgb(0x0F, 0x00, 0x00, 0x00), Color.FromArgb(0x29, 0x00, 0x00, 0x00) };
            public static readonly double[] RimOffsets = { 0, 1 };
        }

        public static class Radius
        {
            public const double Block = 8;
            public const double Row = 4;
            public const double Control = 4;
            public const double Tip = 4;
        }

        public static class Font
        {
            public const string Text = "Segoe UI Variable Text, Segoe UI";
            public const string Display = "Segoe UI Variable Display, Segoe UI";
            public const string Mono = "Cascadia Mono, Consolas";
        }

        public static class Size
        {
            public const double Display = 28;
            public const double Title = 20;
            public const double Heading = 14;
            public const double Body = 14;
            public const double Caption = 12;
            public const double Micro = 12;
        }

        public static class Motion
        {
            public static readonly double[] EaseOut = { 0, 0, 0, 1 };
            public const int PressMs = 83;
            public const int FastMs = 167;
            public const int BaseMs = 250;
            public const int SlowMs = 333;
            public const int RevealMs = 500;
            public const int StaggerMs = 30;
            public const double SpringShapeStiffness = 250;
            public const double SpringShapeDamping = 23;
            public const double SpringFadeStiffness = 340;
            public const double SpringFadeDamping = 37;
        }
    }
}
