// Kapsula — «остров» для Windows. Часть исходного кода: PetPage.cs
// Уголок зверька — вкладка «Зверёк» рядом с «Сегодня». Зверёк сидит на своей подушке, рядом гардероб с нарядами
// за привычки: на каждой вешалке — маленький зверёк в этом наряде. Полученные надеваются щелчком, у остальных
// видно, сколько осталось. Внизу — что выбрано и что уже накоплено: перерывы, задачи, часы музыки, помодоро.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Kapsula
{
    partial class IslandWindow
    {
        PetView roomPet;
        TextBlock roomWearing, roomStats, roomPets;
        readonly Border[] roomSlots = new Border[4];
        readonly PetView[] roomMinis = new PetView[4];
        readonly TextBlock[] roomSlotSub = new TextBlock[4];
        readonly Border[] roomModes = new Border[3];
        static readonly string[] RoomModeIds = { "", "season", "none" };
        static readonly string[] RoomModeNames = { "Сам", "По сезону", "Без наряда" };
        static readonly string[] RoomUnits = { "перерывов", "задач", "музыки", "кругов" };

        FrameworkElement BuildPetPage()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Уголок: зверёк стоит на подушке, под ним — во что он сейчас одет
            var room = new Grid();
            var roomCard = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Child = room };
            var glow = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(75), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 46, 0, 0), IsHitTestVisible = false,
                Background = new RadialGradientBrush(Color.FromArgb(0x30, 0xFF, 0xB0, 0x60), Color.FromArgb(0, 0xFF, 0xB0, 0x60)) };
            room.Children.Add(glow);
            var cushion = new Border { Width = 116, Height = 16, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 156, 0, 0),
                Background = new LinearGradientBrush(Color.FromRgb(0x6A, 0x4F, 0x9A), Color.FromRgb(0x4A, 0x36, 0x70), 90) };
            room.Children.Add(cushion);
            roomPet = new PetView(5, Ink);
            roomPet.Context = "default";
            roomPet.Root.HorizontalAlignment = HorizontalAlignment.Center;
            roomPet.Root.VerticalAlignment = VerticalAlignment.Top;
            roomPet.Root.Margin = new Thickness(roomPet.Root.Children.Count > 1 ? 14 : 0, 113, 0, 0);
            room.Children.Add(roomPet.Root);
            roomWearing = new TextBlock { Foreground = Fg, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 190, 0, 0) };
            room.Children.Add(roomWearing);
            // как его погладить (пока ни разу не гладили) или сколько раз уже гладили
            roomPets = new TextBlock { Foreground = Faint, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 210, 14, 0) };
            room.Children.Add(roomPets);
            room.Children.Add(new TextBlock { Text = "Уголок зверька", Foreground = Faint, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0) });
            g.Children.Add(roomCard);

            // Гардероб: четыре вешалки, под ними — что носить и что накоплено
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new StackPanel { Margin = new Thickness(4, 2, 0, 8) };
            head.Children.Add(new TextBlock { Text = "Гардероб", Foreground = Fg, FontSize = 14, FontWeight = FontWeights.SemiBold });
            head.Children.Add(new TextBlock { Text = "Наряды за привычки — полученный надевается щелчком", Foreground = Dim, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            right.Children.Add(head);
            var slots = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 2 };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var mini = roomMinis[i] = new PetView(3.2, Ink);
                mini.OwnOutfit = OutfitIds[i];   // примерка: этот зверёк всегда в «своём» наряде
                mini.Root.VerticalAlignment = VerticalAlignment.Center;
                mini.Root.HorizontalAlignment = HorizontalAlignment.Center;
                var sg = new Grid();
                sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                sg.Children.Add(mini.Root);
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(new TextBlock { Text = OutfitNames[i], Foreground = Fg, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
                roomSlotSub[i] = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                texts.Children.Add(roomSlotSub[i]);
                Grid.SetColumn(texts, 1); sg.Children.Add(texts);
                var slot = roomSlots[i] = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, i % 2 == 0 ? 6 : 0, i < 2 ? 6 : 0), Child = sg };
                slot.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (!OutfitHave(idx)) { roomMinis[idx].Play("no", 700); return; }   // ещё не заслужен — зверёк на вешалке мотает головой
                    SetOutfit(OutfitIds[idx]);
                    RoomChanged();
                };
                slots.Children.Add(slot);
            }
            Grid.SetRow(slots, 1); right.Children.Add(slots);
            var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            modes.Children.Add(new TextBlock { Text = "Носит:", Foreground = Dim, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 1) });
            for (int m = 0; m < 3; m++)
            {
                int mm = m;
                var b = roomModes[m] = Btn(RoomModeNames[m], false, 0, 26, delegate { SetOutfit(RoomModeIds[mm]); RoomChanged(); });
                b.Margin = new Thickness(0, 0, 6, 0);
                modes.Children.Add(b);
            }
            roomModes[0].ToolTip = "Последний полученный наряд, а пока их нет — сезонный";
            Grid.SetRow(modes, 2); right.Children.Add(modes);
            roomStats = new TextBlock { Foreground = Faint, FontSize = 11.5, Margin = new Thickness(4, 9, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(roomStats, 3); right.Children.Add(roomStats);
            Grid.SetColumn(right, 2); g.Children.Add(right);
            return g;
        }

        // Надели другое — зверёк в уголке радостно подпрыгивает в обновке
        void RoomChanged()
        {
            RenderPetPage();
            if (roomPet != null && !ReducedMotion) roomPet.Play("wow", 700);
        }

        void RenderPetPage()
        {
            if (roomPet == null) return;
            string wear = OutfitWearing(), pick = OutfitChoice();
            for (int i = 0; i < 4; i++)
            {
                bool have = OutfitHave(i), on = wear == OutfitIds[i];
                roomMinis[i].Root.Opacity = have ? 1 : 0.3;
                roomMinis[i].Mood(on ? "happy" : have ? "idle" : "sleep");
                roomSlotSub[i].Text = on ? "надет" : have ? "есть" : OutfitProgress(i) + " " + RoomUnits[i];
                roomSlotSub[i].Foreground = on ? (Brush)accent : Dim;
                roomSlots[i].BorderBrush = on ? (Brush)accent : Line;
                roomSlots[i].Cursor = have ? Cursors.Hand : Cursors.Arrow;
                roomSlots[i].ToolTip = have ? (on ? "Надет" : "Надеть") : "Нужно " + OutfitFor(i);
            }
            for (int m = 0; m < 3; m++) roomModes[m].Background = pick == RoomModeIds[m] ? TabOn : Chip;
            roomWearing.Text = wear == "none" ? "Без наряда" : wear == "" ? (PetView.Season() > 0 ? "В сезонном наряде" : "Без наряда")
                : "В наряде: " + OutfitNames[Array.IndexOf(OutfitIds, wear)].ToLower();
            roomStats.Text = "Накоплено: перерывов " + HabitCount(0) + " · задач " + HabitCount(1) + " · музыки " + HabitCount(2) / 60 + " ч · помодоро " + HabitCount(3);
            int pets;
            int.TryParse(Store.Get("petPets"), out pets);
            roomPets.Text = pets > 0 ? "Гладили " + Plural(pets, "раз", "раза", "раз") : "Погладьте его: поводите курсором туда-сюда";
            roomPets.Foreground = pets > 0 ? (Brush)LovePink : Faint;
        }
    }
}
