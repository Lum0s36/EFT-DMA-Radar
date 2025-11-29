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
using LoneEftDmaRadar.Tarkov.GameWorld.Exits;
using LoneEftDmaRadar.Tarkov.GameWorld.Explosives;
using LoneEftDmaRadar.Tarkov.Unity.Structures; // for Bones enum
using LoneEftDmaRadar.UI.Misc;
using SkiaSharp.Views.WPF;
using CameraManagerNew = LoneEftDmaRadar.Tarkov.GameWorld.Camera.CameraManager;

namespace LoneEftDmaRadar.UI.Skia
{
    public sealed class AimviewWidget : AbstractSKWidget
    {
        // Fields
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
        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;
        private static IReadOnlyCollection<IExplosiveItem> Explosives => Memory.Explosives;
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

                DrawExfils(localPlayer);
                DrawExplosives(localPlayer);
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

        private void DrawExfils(LocalPlayer localPlayer)
        {
            if (Exits is null)
                return;

            const float maxDistance = 25f; // Maximum render distance for non-player entities

            foreach (var exit in Exits)
            {
                if (exit is not Exfil exfil || (exfil.Status != Exfil.EStatus.Open && exfil.Status != Exfil.EStatus.Pending))
                    continue;

                float distance = Vector3.Distance(localPlayer.Position, exfil.Position);
                if (distance > maxDistance)
                    continue;

                if (TryProject(exfil.Position, out var screen))
                {
                    var paint = exfil.Status == Exfil.EStatus.Pending
                        ? SKPaints.PaintExfilPending
                        : SKPaints.PaintExfilOpen;

                    float distanceScale = Math.Clamp(50f / Math.Max(distance, 5f), 0.3f, 2.5f);
                    float r = Math.Clamp(3f * App.Config.UI.UIScale * distanceScale, 2f, 10f);

                    _canvas.DrawCircle(screen.X, screen.Y, r, paint);

                    // Draw exfil name
                    float fontSize = Math.Clamp(SKFonts.EspWidgetFont.Size * distanceScale * 0.8f, 7f, 18f);
                    using var font = new SKFont(SKFonts.EspWidgetFont.Typeface, fontSize) { Subpixel = true };
                    _canvas.DrawText(exfil.Name, new SKPoint(screen.X + r + 3, screen.Y + r + 1), SKTextAlign.Left, font, SKPaints.TextExfil);
                }
            }
        }

        private void DrawExplosives(LocalPlayer localPlayer)
        {
            if (Explosives is null)
                return;

            const float maxDistance = 25f; // Maximum render distance for non-player entities

            foreach (var explosive in Explosives)
            {
                try
                {
                    if (explosive is null || explosive.Position == Vector3.Zero)
                        continue;

                    float distance = Vector3.Distance(localPlayer.Position, explosive.Position);
                    if (distance > maxDistance)
                        continue;

                    if (!TryProject(explosive.Position, out var screen))
                        continue;

                    // Same scaling as loot items
                    float distanceScale = Math.Clamp(25f / Math.Max(distance, 3f), 0.2f, 2.5f);
                    // Same radius range as loot items
                    float r = Math.Clamp(2.5f * App.Config.UI.UIScale * distanceScale, 1.5f, 8f);

                    string label;
                    if (explosive is Tripwire tripwire && tripwire.IsActive)
                    {
                        _canvas.DrawCircle(screen.X, screen.Y, r, SKPaints.PaintExplosives);
                        label = "Tripwire";
                    }
                    else if (explosive is Grenade)
                    {
                        _canvas.DrawCircle(screen.X, screen.Y, r, SKPaints.PaintExplosives);
                        label = "Grenade";
                    }
                    else
                    {
                        continue;
                    }

                    // Draw label with same font sizing as loot
                    float fontSize = Math.Clamp(SKFonts.EspWidgetFont.Size * distanceScale * 0.8f, 7f, 20f);
                    using var font = new SKFont(SKFonts.EspWidgetFont.Typeface, fontSize) { Subpixel = true };
                    var textPaint = new SKPaint
                    {
                        Color = SKPaints.PaintExplosives.Color,
                        IsStroke = false,
                        IsAntialias = true
                    };
                    _canvas.DrawText(label, new SKPoint(screen.X + r + 3, screen.Y + r + 1), SKTextAlign.Left, font, textPaint);
                }
                catch
                {
                    // Skip invalid explosives
                    continue;
                }
            }
        }

        private void DrawPlayersAndAIAsSkeletons(LocalPlayer localPlayer)
        {
            if (!App.Config.AimviewWidget.ShowAI && !App.Config.AimviewWidget.ShowEnemyPlayers)
                return; // Both disabled, skip entirely

            var players = AllPlayers?
                .Where(p => p.IsActive && p.IsAlive && p is not Tarkov.GameWorld.Player.LocalPlayer);

            if (players is null)
                return;

            foreach (var player in players)
            {
                // Filter based on config
                bool isAI = player.IsAI;
                bool isEnemyPlayer = !isAI && player.IsHostile;

                if (isAI && !App.Config.AimviewWidget.ShowAI)
                    continue;
                if (isEnemyPlayer && !App.Config.AimviewWidget.ShowEnemyPlayers)
                    continue;

                float distance = Vector3.Distance(localPlayer.Position, player.Position);
                if (App.Config.UI.MaxDistance > 0 && distance > App.Config.UI.MaxDistance)
                    continue;

                var paint = GetPaint(player);

                // Calculate distance-based scale for line thickness
                float distanceScale = Math.Clamp(50f / Math.Max(distance, 5f), 0.5f, 2.5f);

                foreach (var (from, to) in _boneConnections)
                {
                    var p1 = player.GetBonePos(from);
                    var p2 = player.GetBonePos(to);
                    if (p1 == Vector3.Zero || p2 == Vector3.Zero) continue;
                    if (TryProject(p1, out var s1) && TryProject(p2, out var s2))
                    {
                        // Scale line thickness with distance
                        float t = Math.Max(0.5f, 1.5f * distanceScale);
                        paint.StrokeWidth = t;
                        _canvas.DrawLine(s1.X, s1.Y, s2.X, s2.Y, paint);
                    }
                }
            }
        }

        private void DrawFilteredLoot(LocalPlayer localPlayer)
        {
            if (!App.Config.AimviewWidget.ShowLoot)
                return; // Loot disabled in Aimview

            if (!(App.Config.Loot.Enabled)) return;
            var lootItems = Memory.Game?.Loot?.FilteredLoot;
            if (lootItems is null) return;

            const float maxDistance = 25f; // Maximum render distance for non-player entities

            foreach (var item in lootItems)
            {
                // Filter quest items based on config
                if (item.IsQuestItem && !App.Config.AimviewWidget.ShowQuestItems)
                    continue;

                float distance = Vector3.Distance(localPlayer.Position, item.Position);
                if (distance > maxDistance)
                    continue;

                if (TryProject(item.Position, out var screen))
                {
                    // Calculate distance-based scale with steeper falloff
                    // Reference: 1.0 at 25m, 0.2 at 100m, 2.5 at 10m
                    float distanceScale = Math.Clamp(25f / Math.Max(distance, 3f), 0.2f, 2.5f);

                    // marker scales with distance - smaller base radius
                    float r = Math.Clamp(2.5f * App.Config.UI.UIScale * distanceScale, 1.5f, 8f);
                    
                    // Get color from custom filter or default
                    var paint = SKPaints.PaintFilteredLoot;
                    var textPaint = SKPaints.TextFilteredLoot;
                    var filterColor = item.CustomFilter?.Color;
                    if (!string.IsNullOrEmpty(filterColor) && SKColor.TryParse(filterColor, out var skColor))
                    {
                        paint = new SKPaint
                        {
                            Color = skColor,
                            StrokeWidth = paint.StrokeWidth,
                            Style = paint.Style,
                            IsAntialias = paint.IsAntialias
                        };
                        textPaint = new SKPaint
                        {
                            Color = skColor,
                            IsStroke = false,
                            IsAntialias = true
                        };
                    }

                    _canvas.DrawCircle(screen.X, screen.Y, r, paint);

                    // name label scales with distance, add distance info
                    var shortName = string.IsNullOrWhiteSpace(item.ShortName) ? item.Name : item.ShortName;
                    var label = $"{shortName} D:{distance:F0}m";
                    
                    float fontSize = Math.Clamp(SKFonts.EspWidgetFont.Size * distanceScale * 0.8f, 7f, 20f);
                    using var font = new SKFont(SKFonts.EspWidgetFont.Typeface, fontSize) { Subpixel = true };
                    _canvas.DrawText(label, new SKPoint(screen.X + r + 3, screen.Y + r + 1), SKTextAlign.Left, font, textPaint);
                }
            }
        }

        private void DrawCrosshair()
        {
            // Draw crosshair at widget center
            var bounds = _bitmap.Info.Rect;
            var center = new SKPoint(bounds.MidX, bounds.MidY);
            _canvas.DrawLine(0, center.Y, _bitmap.Width, center.Y, SKPaints.PaintAimviewWidgetCrosshair);
            _canvas.DrawLine(center.X, 0, center.X, _bitmap.Height, SKPaints.PaintAimviewWidgetCrosshair);
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

        // Scale CameraManager coordinates to widget size
        private bool TryProject(in Vector3 world, out SKPoint scr)
        {
            scr = default;
            if (world == Vector3.Zero)
                return false;
            
            // Get projection from CameraManager (uses full ESP viewport)
            if (!CameraManagerNew.WorldToScreen(in world, out var espScreen, onScreenCheck: false, useTolerance: false))
                return false;

            // Get viewport dimensions from CameraManager
            var viewport = CameraManagerNew.Viewport;
            if (viewport.Width <= 0 || viewport.Height <= 0)
                return false;

            // Calculate relative position in viewport (0.0 to 1.0)
            float relX = espScreen.X / viewport.Width;
            float relY = espScreen.Y / viewport.Height;

            // Scale to widget dimensions
            scr = new SKPoint(
                relX * _bitmap.Width,
                relY * _bitmap.Height
            );

            // Check if within widget bounds
            if (scr.X < 0 || scr.X > _bitmap.Width || scr.Y < 0 || scr.Y > _bitmap.Height)
                return false;

            return true;
        }
    }
}