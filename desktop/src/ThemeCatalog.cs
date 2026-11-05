using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CreditMonitor
{
    internal sealed class ThemePreset
    {
        public string Key, Label, Description;
        public string Surface = "#F5182232", SurfaceEnd, Edge = "#3F516A";
        public string Text = "#F1F5F9", Muted = "#95A6BE", Track = "#2D394B";
        public string MetricSurface = "#08FFFFFF", MetricEdge = "#203F516A";
        public string Hover = "#3D506B", Pressed = "#35465D", Accent = "#818CF8";
        public double Radius = 18, ButtonRadius = 8, BarRadius = 4;
        public double BadgeRadiusLimit = Double.PositiveInfinity;
        public bool Light, Pixel, Elevated;
        public string Good,Warning,Bad;
        public string MetricColor(double remaining){return remaining>=60?(Good??(Light?"#137A49":"#4ADE80")):remaining>=25?(Warning??(Light?"#916000":"#FACC15")):(Bad??(Light?"#B52547":"#FB7185"));}

        public Brush SurfaceBrush()
        {
            Color start = (Color)ColorConverter.ConvertFromString(Surface);
            if (SurfaceEnd == null) return new SolidColorBrush(start);
            return new LinearGradientBrush(start, (Color)ColorConverter.ConvertFromString(SurfaceEnd), new Point(0, 0), new Point(1, 1));
        }
    }

    internal static class ThemeCatalog
    {
        public static readonly ThemePreset[] All = {
            new ThemePreset { Key = "Original", Label = "Dark · Original", Description = "กรมท่า · แบบดั้งเดิม", Accent = "#4ADE80" },
            new ThemePreset { Key = "Light", Label = "Light", Description = "ฟ้าใส · การ์ดขาว · แถบสีน้ำเงิน", Light = true,
                Surface = "#FFE6F1FF", Edge = "#A6C2E5", Text = "#142E51", Muted = "#466184", Track = "#D3E3F8",
                MetricSurface = "#FFFFFFFF", MetricEdge = "#B8CDEA", Hover = "#C7DEFF", Pressed = "#ACCDFB", Accent = "#1D4ED8", Good="#1D4ED8", Warning="#9A5800", Bad="#B42348" },
            new ThemePreset { Key = "Minimal", Label = "Minimal", Description = "กระดาษครีม · ขาวดำ · เส้นคั่น", Light = true,
                Surface = "#FFFAF8F2", Edge = "#D4CFC4", Text = "#242321", Muted = "#68635B", Track = "#E4E0D8",
                MetricSurface = "#00FFFFFF", MetricEdge = "#D4CFC4", Hover = "#EAE5DB", Pressed = "#DDD7CB", Accent = "#242321", Good="#242321", Warning="#57534E", Bad="#A32E31",
                Radius = 4, ButtonRadius = 2, BarRadius = 1, BadgeRadiusLimit = 2 },
            new ThemePreset { Key = "Modern", Label = "Modern", Description = "น้ำเงินเข้ม · มิติร่วมสมัย",
                Surface = "#FF15243E", SurfaceEnd = "#FF25274A", Edge = "#586EAE", Text = "#F7F9FF", Muted = "#B9C9E8",
                Track = "#425071", MetricSurface = "#263E568D", MetricEdge = "#405B76B2", Hover = "#455B94", Pressed = "#32456F",
                Accent = "#A5B4FC", Radius = 24, ButtonRadius = 10, BarRadius = 6 },
            new ThemePreset { Key = "Glass", Label = "Glass", Description = "โปร่งใส · ขอบสะท้อนแสง",
                Surface = "#DB2F3B57", SurfaceEnd = "#D9433A61", Edge = "#ADCAD5EF", Text = "#FFFFFF", Muted = "#E1E8F6",
                Track = "#AA51607C", MetricSurface = "#22FFFFFF", MetricEdge = "#50FFFFFF", Hover = "#705B749E", Pressed = "#80506488",
                Accent = "#C4D9FF", Radius = 22, ButtonRadius = 10, BarRadius = 5 },
            new ThemePreset { Key = "Material", Label = "Material", Description = "ลาเวนเดอร์ · การ์ดลอย · ป้ายสีม่วง", Light = true, Elevated = true,
                Surface = "#FFEDE4FF", SurfaceEnd="#FFF8F1FF", Edge = "#B7A1D9", Text = "#302044", Muted = "#6D587D", Track = "#DED1F5",
                MetricSurface = "#FFFFFBFF", MetricEdge = "#D9C7EF", Hover = "#D8C3F7", Pressed = "#C5A9EC", Accent = "#6D28D9", Good="#6D28D9", Warning="#A14C11", Bad="#AA2468",
                Radius = 8, ButtonRadius = 6, BarRadius = 6, BadgeRadiusLimit = 8 },
            new ThemePreset { Key = "Gradient", Label = "Gradient", Description = "ไล่สีม่วง–ชมพู · สดใส",
                Surface = "#FF34245A", SurfaceEnd = "#FF672846", Edge = "#B277C9", Text = "#FFFFFF", Muted = "#EFCEE9",
                Track = "#78537F", MetricSurface = "#1CFFFFFF", MetricEdge = "#50D5AADF", Hover = "#946097", Pressed = "#744A80",
                Accent = "#F9A8D4", Radius = 22, ButtonRadius = 10, BarRadius = 6 },
            new ThemePreset { Key="AnimeNight",Label="Anime Night",Description="ภาพต้นฉบับ · ชมพูและม่วง",Surface="#FF211C36",Edge="#7563A4",Text="#F7F1FF",Muted="#C6B4DD",Track="#443852",Hover="#4F386E",Accent="#F9A8D4",MetricSurface="#252138",Radius=8 },
            new ThemePreset { Key="PixelRPG",Label="Pixel RPG",Description="พิกเซล · น้ำเงินและทอง",Surface="#FF17243A",Edge="#7A9DC4",Text="#FFF1CB",Muted="#B8C9DE",Track="#344967",Hover="#355079",Accent="#F6C86B",MetricSurface="#21324B",Pixel=true,Radius=0,ButtonRadius=0,BarRadius=0 },
            new ThemePreset { Key="Trader",Label="Trader",Description="กราฟ · เทาเข้มและฟ้า",Surface="#FF0D1C23",Edge="#38555E",Text="#EAF5F7",Muted="#9CB6BD",Track="#2A404A",Hover="#2C4754",Accent="#67D3E9",MetricSurface="#13262E",Radius=6 },
            new ThemePreset { Key="Business",Label="Business",Description="เอกสาร · ขาวและน้ำเงิน",Surface="#FFF6F8FC",Edge="#CAD4E2",Text="#17253B",Muted="#50617B",Track="#D9E0EA",Hover="#DDE7F5",Accent="#2563EB",MetricSurface="#EDF2F9",Light=true,Radius=6 },
            new ThemePreset { Key="Dev",Label="Dev",Description="เทอร์มินัล · เทาเข้มและม่วง",Surface="#FF18191F",Edge="#454653",Text="#E9E9F2",Muted="#B0AEC5",Track="#373843",Hover="#37334C",Accent="#B4A6FA",MetricSurface="#22232B",Radius=6 },
            new ThemePreset { Key = "Minecraft", Label = "Pixel Classic", Description = "พิกเซล · ธีมเดิม", Pixel = true,
                Surface = "#FF262E24", Edge = "#738867", Text = "#F2F5D7", Muted = "#C0CCAD", Track = "#454D3C",
                MetricSurface = "#1A9CB18A", MetricEdge = "#606E865E", Hover = "#4A5941", Pressed = "#36442E", Accent = "#AFC98C",
                Radius = 0, ButtonRadius = 0, BarRadius = 0, BadgeRadiusLimit = 0 }
        };

        public static bool Contains(string key) { return All.Any(p => p.Key == key); }
        public static ThemePreset Find(string key) { return All.FirstOrDefault(p => p.Key == key) ?? All[0]; }
    }
}
