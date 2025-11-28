/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Structures; // for Bones enum
using LoneEftDmaRadar.UI.Misc;
using SkiaSharp.Views.WPF;

namespace LoneEftDmaRadar.UI.Skia
{
    public sealed class AimviewWidget : AbstractSKWidget
    {
        // Fields
        private Vector3 _forward, _right, _up, _camPos;
        private SKBitmap _bitmap;
        private SKCanvas _canvas;

        public AimviewWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "Aimview",
                new SKPoint(location.Left, location.Top),
                new SKSize(location.Width, location.Height),
                scale)
        {
            AllocateSurface((int)location.Width, (int)location.Height);
            Minimized = minimized;
        }

        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;
        private static IReadOnlyCollection<AbstractPlayer> AllPlayers => Memory.Players;
        private static bool InRaid => Memory.InRaid;

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
            if (Minimized)
                return;

            RenderESPWidget(canvas, ClientRectangle);
        }

        private void RenderESPWidget(SKCanvas targetCanvas, SKRect dest)
        {
            EnsureSurface(Size);

            _canvas.Clear(SKColors.Transparent);

            try
            {
                if (!InRaid)
                    return;

                if (LocalPlayer is not LocalPlayer localPlayer)
                    return;

                // Precompute scale factors once per frame
                UpdateMatrix(localPlayer);

                DrawPlayersAndAIAsSkeletons(localPlayer);
                DrawFilteredLoot(localPlayer);
                DrawCrosshair();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"CRITICAL AIMVIEW WIDGET RENDER ERROR: {ex}");
            }

            _canvas.Flush();
            targetCanvas.DrawBitmap(_bitmap, dest, SKPaints.PaintBitmap);
        }

        private void UpdateMatrix(LocalPlayer lp)
        {
            float yaw = lp.Rotation.X * (MathF.PI / 180f);   // horizontal
            float pitch = lp.Rotation.Y * (MathF.PI / 180f);   // vertical

            float cy = MathF.Cos(yaw);
            float sy = MathF.Sin(yaw);
            float cp = MathF.Cos(pitch);
            float sp = MathF.Sin(pitch);

            _forward = new Vector3(
                sy * cp,   // X
               -sp,        // Y (up/down tilt)
                cy * cp    // Z
            );
            _forward = Vector3.Normalize(_forward);

            _right = new Vector3(cy, 0f, -sy);
            _right = Vector3.Normalize(_right);

            _up = Vector3.Normalize(Vector3.Cross(_right, _forward));

            _up = -_up;

            _camPos = lp.Position;
        }

        // Skeleton connections reused for Aimview
        private static readonly (Bones From, Bones To)[] _boneConnections = new[]
        {
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),
            // Left Arm
            (Bones.HumanNeck, Bones.HumanLUpperarm),
            (Bones.HumanLUpperarm, Bones.HumanLForearm1),
            (Bones.HumanLForearm1, Bones.HumanLForearm2),
            (Bones.HumanLForearm2, Bones.HumanLPalm),
            // Right Arm
            (Bones.HumanNeck, Bones.HumanRUpperarm),
            (Bones.HumanRUpperarm, Bones.HumanRForearm1),
            (Bones.HumanRForearm1, Bones.HumanRForearm2),
            (Bones.HumanRForearm2, Bones.HumanRPalm),
            // Left Leg
            (Bones.HumanPelvis, Bones.HumanLThigh1),
            (Bones.HumanLThigh1, Bones.HumanLThigh2),
            (Bones.HumanLThigh2, Bones.HumanLCalf),
            (Bones.HumanLCalf, Bones.HumanLFoot),
            // Right Leg
            (Bones.HumanPelvis, Bones.HumanRThigh1),
            (Bones.HumanRThigh1, Bones.HumanRThigh2),
            (Bones.HumanRThigh2, Bones.HumanRCalf),
            (Bones.HumanRCalf, Bones.HumanRFoot),
        };

        private void DrawPlayersAndAIAsSkeletons(LocalPlayer localPlayer)
        {
            var players = AllPlayers?
                .Where(p => p.IsActive && p.IsAlive && p is not Tarkov.GameWorld.Player.LocalPlayer);

            if (players is null)
                return;

            foreach (var player in players)
            {
                float distance = Vector3.Distance(localPlayer.Position, player.Position);
                if (App.Config.UI.MaxDistance > 0 && distance > App.Config.UI.MaxDistance)
                    continue;

                var paint = GetPaint(player);
                float thickness = Math.Max(1f, SKPaints.PaintAimviewWidgetPMC.StrokeWidth);

                foreach (var (from, to) in _boneConnections)
                {
                    var p1 = player.GetBonePos(from);
                    var p2 = player.GetBonePos(to);
                    if (p1 == Vector3.Zero || p2 == Vector3.Zero) continue;
                    if (TryProject(p1, out var s1) && TryProject(p2, out var s2))
                    {
                        _canvas.DrawLine(s1.X, s1.Y, s2.X, s2.Y, paint);
                    }
                }
            }
        }

        private void DrawFilteredLoot(LocalPlayer localPlayer)
        {
            if (!(App.Config.Loot.Enabled)) return;
            var lootItems = Memory.Game?.Loot?.FilteredLoot;
            if (lootItems is null) return;

            foreach (var item in lootItems)
            {
                float distance = Vector3.Distance(localPlayer.Position, item.Position);
                if (App.Config.UI.EspLootMaxDistance > 0 && distance > App.Config.UI.EspLootMaxDistance)
                    continue;

                if (TryProject(item.Position, out var screen))
                {
                    // small marker
                    _canvas.DrawCircle(screen.X, screen.Y, 3f * App.Config.UI.UIScale, SKPaints.PaintFilteredLoot);
                    // name label
                    var shortName = string.IsNullOrWhiteSpace(item.ShortName) ? item.Name : item.ShortName;
                    _canvas.DrawText(shortName, new SKPoint(screen.X + 6, screen.Y + 2), SKTextAlign.Left, SKFonts.EspWidgetFont, SKPaints.TextFilteredLoot);
                }
            }
        }

        private void DrawCrosshair()
        {
            var bounds = _bitmap.Info.Rect;
            float centerX = bounds.MidX;
            float centerY = bounds.MidY;

            _canvas.DrawLine(bounds.Left, centerY, bounds.Right, centerY, SKPaints.PaintAimviewWidgetCrosshair);
            _canvas.DrawLine(centerX, bounds.Top, centerX, bounds.Bottom, SKPaints.PaintAimviewWidgetCrosshair);
        }

        private void EnsureSurface(SKSize size)
        {
            if (_bitmap != null &&
                _canvas != null &&
                _bitmap.Width == (int)size.Width &&
                _bitmap.Height == (int)size.Height)
                return;

            DisposeSurface();
            AllocateSurface((int)size.Width, (int)size.Height);
        }

        private void AllocateSurface(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return;

            _bitmap = new SKBitmap(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            _canvas = new SKCanvas(_bitmap);
        }

        private void DisposeSurface()
        {
            _canvas?.Dispose();
            _canvas = null;
            _bitmap?.Dispose();
            _bitmap = null;
        }

        public override void SetScaleFactor(float newScale)
        {
            base.SetScaleFactor(newScale);
            // Consolidated strokes
            float std = 1f * newScale;
            SKPaints.PaintAimviewWidgetCrosshair.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetLocalPlayer.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetPMC.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetWatchlist.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetStreamer.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetTeammate.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetBoss.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetScav.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetRaider.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetPScav.StrokeWidth = std;
            SKPaints.PaintAimviewWidgetFocused.StrokeWidth = std;
        }

        public override void Dispose()
        {
            DisposeSurface();
            base.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPaint GetPaint(AbstractPlayer player)
        {
            if (player.IsFocused)
                return SKPaints.PaintAimviewWidgetFocused;
            if (player is LocalPlayer)
                return SKPaints.PaintAimviewWidgetLocalPlayer;

            return player.Type switch
            {
                PlayerType.Teammate => SKPaints.PaintAimviewWidgetTeammate,
                PlayerType.PMC => SKPaints.PaintAimviewWidgetPMC,
                PlayerType.AIScav => SKPaints.PaintAimviewWidgetScav,
                PlayerType.AIRaider => SKPaints.PaintAimviewWidgetRaider,
                PlayerType.AIBoss => SKPaints.PaintAimviewWidgetBoss,
                PlayerType.PScav => SKPaints.PaintAimviewWidgetPScav,
                PlayerType.SpecialPlayer => SKPaints.PaintAimviewWidgetWatchlist,
                PlayerType.Streamer => SKPaints.PaintAimviewWidgetStreamer,
                _ => SKPaints.PaintAimviewWidgetPMC
            };
        }

        private bool TryProject(in Vector3 world, out SKPoint scr)
        {
            scr = default;
            var dir = world - _camPos;

            float dz = Vector3.Dot(dir, _forward);
            if (dz <= 0f)
                return false;

            float dx = Vector3.Dot(dir, _right);
            float dy = Vector3.Dot(dir, _up);

            // Perspective divide
            float nx = dx / dz;
            float ny = dy / dz;

            const float PSEUDO_FOV = 1.0f;
            nx /= PSEUDO_FOV;
            ny /= PSEUDO_FOV;

            float w = _bitmap.Width;
            float h = _bitmap.Height;

            scr.X = w * 0.5f + nx * (w * 0.5f);
            scr.Y = h * 0.5f - ny * (h * 0.5f);

            return !(scr.X < 0 || scr.X > w || scr.Y < 0 || scr.Y > h);
        }
    }
}