// Generated from tokens/tokens.json (v0.1.0) by tools/build.js. Do not edit by hand.
using System.Windows.Media;

namespace DevTxt
{
    static class Tokens
    {
        public const string Version = "0.1.0";

        public static class Palette
        {
            public static readonly Color Brand = Color.FromRgb(0xF0, 0x8A, 0x4B);
            public static readonly Color Blue = Color.FromRgb(0x0A, 0x84, 0xFF);
            public static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);
            public static readonly Color Red = Color.FromRgb(0xFF, 0x45, 0x3A);
            public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
            public static readonly Color Yellow = Color.FromRgb(0xFF, 0xD6, 0x0A);
            public static readonly Color Purple = Color.FromRgb(0x8A, 0x5C, 0xF6);
            public static readonly Color Pink = Color.FromRgb(0xFF, 0x37, 0x5F);
            public static readonly Color Accent = Blue;
        }

        public static class Dark
        {
            public static readonly Color Bg = Color.FromRgb(0x09, 0x09, 0x0D);
            public static readonly Color Surface = Color.FromRgb(0x15, 0x15, 0x18);
            public static readonly Color Raised = Color.FromRgb(0x2C, 0x2C, 0x31);
            public static readonly Color Label = Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF);
            public static readonly Color Label2 = Color.FromArgb(0xA3, 0xFF, 0xFF, 0xFF);
            public static readonly Color Label3 = Color.FromArgb(0x6B, 0xFF, 0xFF, 0xFF);
            public static readonly Color Separator = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
            public static readonly Color Fill = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
            public static readonly Color Fill2 = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
            public static readonly Color FillPress = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF);
            public static readonly Color Edge = Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF);
            public static readonly Color EdgeTop = Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF);
            public static readonly Color Menu = Color.FromArgb(0xD1, 0x1E, 0x1E, 0x26);
            public static readonly Color Tooltip = Color.FromArgb(0xF2, 0x24, 0x24, 0x27);
        }

        public static class GlassDark
        {
            public const double FillTop = 0.15;
            public const double FillBottom = 0.075;
            public const double HoverTop = 0.22;
            public const double HoverBottom = 0.12;
            public const double StrongTop = 0.27;
            public const double StrongBottom = 0.16;
            public static readonly Color[] RimColors = { Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF) };
            public static readonly double[] RimOffsets = { 0, 0.32, 0.68, 1 };
        }

        public static class Light
        {
            public static readonly Color Bg = Color.FromRgb(0xF4, 0xF4, 0xF6);
            public static readonly Color Surface = Color.FromRgb(0xFF, 0xFF, 0xFF);
            public static readonly Color Raised = Color.FromRgb(0xFF, 0xFF, 0xFF);
            public static readonly Color Label = Color.FromArgb(0xE0, 0x00, 0x00, 0x00);
            public static readonly Color Label2 = Color.FromArgb(0x99, 0x00, 0x00, 0x00);
            public static readonly Color Label3 = Color.FromArgb(0x66, 0x00, 0x00, 0x00);
            public static readonly Color Separator = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
            public static readonly Color Fill = Color.FromArgb(0x0D, 0x00, 0x00, 0x00);
            public static readonly Color Fill2 = Color.FromArgb(0x17, 0x00, 0x00, 0x00);
            public static readonly Color FillPress = Color.FromArgb(0x24, 0x00, 0x00, 0x00);
            public static readonly Color Edge = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
            public static readonly Color EdgeTop = Color.FromRgb(0xFF, 0xFF, 0xFF);
            public static readonly Color Menu = Color.FromArgb(0xDB, 0xFA, 0xFA, 0xFC);
            public static readonly Color Tooltip = Color.FromArgb(0xF2, 0x24, 0x24, 0x27);
        }

        public static class GlassLight
        {
            public const double FillTop = 0.86;
            public const double FillBottom = 0.66;
            public const double HoverTop = 0.96;
            public const double HoverBottom = 0.8;
            public const double StrongTop = 1;
            public const double StrongBottom = 0.9;
            public static readonly Color[] RimColors = { Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0x12, 0x00, 0x00, 0x00), Color.FromArgb(0x0F, 0x00, 0x00, 0x00), Color.FromArgb(0x1F, 0x00, 0x00, 0x00) };
            public static readonly double[] RimOffsets = { 0, 0.32, 0.68, 1 };
        }

        public static class Radius
        {
            public const double Block = 18;
            public const double Row = 14;
            public const double Control = 11;
            public const double Tip = 9;
        }

        public static class Font
        {
            public const string Text = "Segoe UI Variable Text, Segoe UI";
            public const string Display = "Segoe UI Variable Display, Segoe UI";
            public const string Mono = "Cascadia Mono, Consolas";
        }

        public static class Size
        {
            public const double Display = 26;
            public const double Title = 17;
            public const double Heading = 13.5;
            public const double Body = 12.5;
            public const double Caption = 11.5;
            public const double Micro = 10.5;
        }

        public static class Motion
        {
            public static readonly double[] EaseOut = { 0.22, 1, 0.36, 1 };
            public const int PressMs = 90;
            public const int FastMs = 180;
            public const int BaseMs = 230;
            public const int SlowMs = 380;
            public const int RevealMs = 560;
            public const int StaggerMs = 45;
            public const double SpringShapeStiffness = 250;
            public const double SpringShapeDamping = 23;
            public const double SpringFadeStiffness = 340;
            public const double SpringFadeDamping = 37;
        }
    }
}
