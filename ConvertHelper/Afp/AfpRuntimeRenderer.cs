using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.PixelFormats;

namespace ConvertHelper.Afp
{
    internal sealed class AfpRuntimeRenderer
    {
        private abstract class Definition
        {
            public int TagId;
        }

        private sealed class ShapeDefinition : Definition
        {
            public AfpShape Shape;
        }

        private sealed class ClipDefinition : Definition
        {
            public AfpTimeline Timeline;
        }

        private sealed class DummyDefinition : Definition { }

        private abstract class PlacedObject
        {
            public int ObjectId;
            public int Depth;
            public Definition Source;
            public AfpPoint RotationOrigin;
            public AfpMatrix Transform;
            public int Projection;
            public AfpColor MultiplyColor;
            public AfpColor AddColor;
            public AfpHsl HslShift;
            public int Blend;
            public bool Visible = true;
        }

        private sealed class PlacedShape : PlacedObject { }

        private sealed class PlacedClip : PlacedObject
        {
            public ClipDefinition ClipSource;
            public List<PlacedObject> Children = new List<PlacedObject>();
            public int Frame;

            public bool Finished => Frame == ClipSource.Timeline.Frames.Count;
            public void Rewind()
            {
                Frame = 0;
                Children.Clear();
            }
        }

        private readonly Dictionary<string, AfpMovie> movies;
        private readonly Dictionary<string, AfpShape> shapes;
        private readonly Dictionary<string, AfpTexture> textures;
        private readonly Dictionary<string, Definition> definitions = new Dictionary<string, Definition>();
        private readonly Dictionary<string, AfpTexture> rectangleTextures = new Dictionary<string, AfpTexture>();

        public AfpRuntimeRenderer(
            Dictionary<string, AfpMovie> movies,
            Dictionary<string, AfpShape> shapes,
            Dictionary<string, AfpTexture> textures)
        {
            this.movies = movies;
            this.shapes = shapes;
            this.textures = textures;
            foreach (AfpMovie movie in movies.Values)
                RegisterDefinitions(movie.Root);
            foreach (AfpMovie movie in movies.Values)
                RegisterImports(movie);
        }

        public IEnumerable<Rgba32[]> Render(AfpMovie movie)
        {
            ClipDefinition rootDefinition = new ClipDefinition { Timeline = movie.Root };
            PlacedClip root = NewClip(-1, -1, rootDefinition, AfpMatrix.Identity(), AfpPlaceTag.ProjectionAffine,
                AfpColor.White, AfpColor.Transparent, AfpHsl.Zero, 0, AfpPoint.Zero);

            while (!root.Finished)
            {
                ProcessClip(root, true);
                Rgba32[] canvas = new Rgba32[movie.Width * movie.Height];
                RenderObject(canvas, movie.Width, movie.Height, root, AfpMatrix.Identity(),
                    AfpPlaceTag.ProjectionAffine, AfpColor.White, AfpColor.Transparent, AfpHsl.Zero, 0);
                yield return canvas;
            }
        }

        private static string Key(string movie, int id) => movie + "\0" + id;

        private void RegisterDefinitions(AfpTimeline timeline)
        {
            foreach (AfpTag tag in timeline.Tags)
            {
                if (tag is AfpShapeTag shapeTag)
                {
                    if (!shapes.TryGetValue(shapeTag.Reference, out AfpShape shape))
                        throw new InvalidOperationException("GEO shape was not found: " + shapeTag.Reference);
                    definitions[Key(timeline.MovieName, shapeTag.Id)] = new ShapeDefinition
                    {
                        TagId = shapeTag.Id,
                        Shape = shape,
                    };
                }
                else if (tag is AfpSpriteTag spriteTag)
                {
                    definitions[Key(timeline.MovieName, spriteTag.Id)] = new ClipDefinition
                    {
                        TagId = spriteTag.Id,
                        Timeline = spriteTag.Timeline,
                    };
                    RegisterDefinitions(spriteTag.Timeline);
                }
            }
        }

        private void RegisterImports(AfpMovie movie)
        {
            foreach (KeyValuePair<int, AfpImport> pair in movie.ImportedTags)
            {
                Definition imported = FindExport(pair.Value);
                definitions[Key(movie.ExportedName, pair.Key)] = imported ?? new DummyDefinition { TagId = pair.Key };
            }
        }

        private Definition FindExport(AfpImport import)
        {
            if (!movies.TryGetValue(import.SwfName, out AfpMovie sourceMovie))
                return null;
            if (!sourceMovie.ExportedTags.TryGetValue(import.TagName, out int tagId))
                return null;
            if (sourceMovie.ImportedTags.TryGetValue(tagId, out AfpImport nested))
                return FindExport(nested);
            return definitions.TryGetValue(Key(sourceMovie.ExportedName, tagId), out Definition definition)
                ? definition
                : null;
        }

        private void ProcessClip(PlacedClip clip, bool root)
        {
            if (clip.Finished)
            {
                if (root) return;
                clip.Rewind();
            }

            List<PlacedClip> existingChildren = clip.Children.OfType<PlacedClip>().ToList();
            AfpFrame frame = clip.ClipSource.Timeline.Frames[clip.Frame];
            int end = Math.Min(frame.StartTag + frame.TagCount, clip.ClipSource.Timeline.Tags.Count);
            for (int i = frame.StartTag; i < end; i++)
            {
                PlacedClip newClip = ApplyTag(clip, clip.ClipSource.Timeline.Tags[i]);
                if (newClip != null)
                    ProcessClip(newClip, false);
            }
            clip.Frame++;

            foreach (PlacedClip child in existingChildren)
                ProcessClip(child, false);
        }

        private PlacedClip ApplyTag(PlacedClip parent, AfpTag tag)
        {
            if (tag is AfpPlaceTag place)
            {
                if (place.Update)
                {
                    for (int i = parent.Children.Count - 1; i >= 0; i--)
                    {
                        PlacedObject current = parent.Children[i];
                        if (current.ObjectId != place.ObjectId || current.Depth != place.Depth) continue;

                        AfpColor mult = place.MultiplyColor ?? current.MultiplyColor;
                        AfpColor add = place.AddColor ?? current.AddColor;
                        AfpHsl hsl = place.HslShift ?? current.HslShift;
                        AfpMatrix transform = place.Transform != null && place.Projection != AfpPlaceTag.ProjectionNone
                            ? current.Transform.Update(place.Transform, place.Projection == AfpPlaceTag.ProjectionPerspective)
                            : current.Transform;
                        AfpPoint origin = place.RotationOrigin ?? current.RotationOrigin;
                        int blend = place.Blend ?? current.Blend;
                        int projection = place.Projection != AfpPlaceTag.ProjectionNone ? place.Projection : current.Projection;

                        if (place.SourceTagId.HasValue && place.SourceTagId.Value != current.Source.TagId)
                        {
                            Definition source = ResolveDefinition(parent.ClipSource.Timeline.MovieName, place.SourceTagId.Value);
                            PlacedObject replacement = NewPlaced(place.ObjectId, place.Depth, source, transform, projection,
                                mult, add, hsl, blend, origin);
                            parent.Children[i] = replacement;
                            return replacement as PlacedClip;
                        }

                        current.MultiplyColor = mult;
                        current.AddColor = add;
                        current.HslShift = hsl;
                        current.Transform = transform;
                        current.RotationOrigin = origin;
                        current.Blend = blend;
                        current.Projection = projection;
                        return null;
                    }
                    return null;
                }

                if (!place.SourceTagId.HasValue)
                    throw new InvalidOperationException("PlaceObject create tag has no source ID.");
                Definition definition = ResolveDefinition(parent.ClipSource.Timeline.MovieName, place.SourceTagId.Value);
                PlacedObject placed = NewPlaced(place.ObjectId, place.Depth, definition,
                    place.Transform ?? AfpMatrix.Identity(), place.Projection,
                    place.MultiplyColor ?? AfpColor.White, place.AddColor ?? AfpColor.Transparent,
                    place.HslShift ?? AfpHsl.Zero, place.Blend ?? 0, place.RotationOrigin ?? AfpPoint.Zero);
                parent.Children.Add(placed);
                return placed as PlacedClip;
            }

            if (tag is AfpRemoveTag remove)
            {
                if (remove.ObjectId != 0)
                {
                    parent.Children.RemoveAll(x => x.ObjectId == remove.ObjectId && x.Depth == remove.Depth);
                }
                else
                {
                    for (int i = parent.Children.Count - 1; i >= 0; i--)
                    {
                        if (parent.Children[i].Depth != remove.Depth) continue;
                        parent.Children.RemoveAt(i);
                        break;
                    }
                }
            }
            return null;
        }

        private Definition ResolveDefinition(string movieName, int id)
        {
            if (definitions.TryGetValue(Key(movieName, id), out Definition definition))
                return definition;
            return new DummyDefinition { TagId = id };
        }

        private static PlacedObject NewPlaced(int objectId, int depth, Definition definition, AfpMatrix transform,
            int projection, AfpColor mult, AfpColor add, AfpHsl hsl, int blend, AfpPoint origin)
        {
            if (definition is ClipDefinition clip)
                return NewClip(objectId, depth, clip, transform, projection, mult, add, hsl, blend, origin);
            if (definition is ShapeDefinition)
                return new PlacedShape
                {
                    ObjectId = objectId, Depth = depth, Source = definition, RotationOrigin = origin,
                    Transform = transform, Projection = projection, MultiplyColor = mult, AddColor = add,
                    HslShift = hsl, Blend = blend,
                };
            return new PlacedShape
            {
                ObjectId = objectId, Depth = depth, Source = definition, RotationOrigin = origin,
                Transform = transform, Projection = projection, MultiplyColor = mult, AddColor = add,
                HslShift = hsl, Blend = blend, Visible = false,
            };
        }

        private static PlacedClip NewClip(int objectId, int depth, ClipDefinition definition, AfpMatrix transform,
            int projection, AfpColor mult, AfpColor add, AfpHsl hsl, int blend, AfpPoint origin)
        {
            return new PlacedClip
            {
                ObjectId = objectId, Depth = depth, Source = definition, ClipSource = definition,
                RotationOrigin = origin, Transform = transform, Projection = projection,
                MultiplyColor = mult, AddColor = add, HslShift = hsl, Blend = blend,
            };
        }

        private void RenderObject(Rgba32[] canvas, int width, int height, PlacedObject placed,
            AfpMatrix parentTransform, int parentProjection, AfpColor parentMult, AfpColor parentAdd,
            AfpHsl parentHsl, int parentBlend)
        {
            if (!placed.Visible) return;

            AfpMatrix transform = placed.Transform.Multiply(parentTransform)
                .Translate(AfpPoint.Zero.Subtract(placed.RotationOrigin));
            int projection = parentProjection == AfpPlaceTag.ProjectionPerspective
                ? AfpPlaceTag.ProjectionPerspective
                : placed.Projection;
            AfpColor mult = placed.MultiplyColor.Multiply(parentMult);
            AfpColor add = placed.AddColor.Multiply(parentMult).Add(parentAdd);
            AfpHsl hsl = placed.HslShift.Add(parentHsl);
            int blend = placed.Blend;
            if (parentBlend != 0 && parentBlend != 1 && parentBlend != 2 && (blend == 0 || blend == 1 || blend == 2))
                blend = parentBlend;

            if (placed is PlacedClip clip)
            {
                foreach (int depth in clip.Children.Select(x => x.Depth).Distinct().OrderBy(x => x))
                {
                    foreach (PlacedObject child in clip.Children)
                    {
                        if (child.Depth == depth)
                            RenderObject(canvas, width, height, child, transform, projection, mult, add, hsl, blend);
                    }
                }
                return;
            }

            if (!(placed.Source is ShapeDefinition shapeDefinition)) return;
            AfpShape shape = shapeDefinition.Shape;
            foreach (AfpDrawParams draw in shape.DrawParams)
            {
                if ((draw.Flags & 1) == 0) continue;
                AfpTexture texture = null;
                if ((draw.Flags & 2) != 0)
                {
                    textures.TryGetValue(draw.TextureName, out texture);
                }
                else if ((draw.Flags & 8) != 0 && draw.BlendColor.HasValue)
                {
                    texture = GetRectangleTexture(shape, draw.BlendColor.Value);
                }
                if (texture != null)
                    Composite(canvas, width, height, texture, transform, add, mult, hsl, blend);
            }
        }

        private AfpTexture GetRectangleTexture(AfpShape shape, AfpColor color)
        {
            string key = shape.Reference + ":" + color.R + ":" + color.G + ":" + color.B + ":" + color.A;
            if (rectangleTextures.TryGetValue(key, out AfpTexture cached)) return cached;
            Rgba32 pixel = ToPixel(color);
            Rgba32[] pixels = Enumerable.Repeat(pixel, shape.Width * shape.Height).ToArray();
            cached = new AfpTexture { Name = key, Width = shape.Width, Height = shape.Height, Pixels = pixels };
            rectangleTextures[key] = cached;
            return cached;
        }

        private static void Composite(Rgba32[] canvas, int width, int height, AfpTexture texture,
            AfpMatrix transform, AfpColor add, AfpColor mult, AfpHsl hsl, int blend)
        {
            if (!transform.TryInverseAffine(out AfpMatrix inverse)) return;
            AfpPoint p1 = transform.MultiplyPoint(AfpPoint.Zero);
            AfpPoint p2 = transform.MultiplyPoint(new AfpPoint(texture.Width, 0));
            AfpPoint p3 = transform.MultiplyPoint(new AfpPoint(0, texture.Height));
            AfpPoint p4 = transform.MultiplyPoint(new AfpPoint(texture.Width, texture.Height));
            int minX = Math.Max((int)Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X)), 0);
            int maxX = Math.Min((int)Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X)) + 1, width);
            int minY = Math.Max((int)Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y)), 0);
            int maxY = Math.Min((int)Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y)) + 1, height);
            if (minX >= maxX || minY >= maxY) return;
            bool identityColor = add.R == 0.0 && add.G == 0.0 && add.B == 0.0 && add.A == 0.0 &&
                mult.R == 1.0 && mult.G == 1.0 && mult.B == 1.0 && mult.A == 1.0 && hsl.IsIdentity;

            if (inverse.A12 == 0.0 && inverse.A21 == 0.0)
            {
                int[] sourceXs = new int[maxX - minX];
                for (int x = minX; x < maxX; x++)
                    sourceXs[x - minX] = (int)(inverse.A11 * (x + 0.5) + inverse.A41);

                for (int y = minY; y < maxY; y++)
                {
                    int sourceY = (int)(inverse.A22 * (y + 0.5) + inverse.A42);
                    if (sourceY < 0 || sourceY >= texture.Height)
                        continue;

                    int destinationOffset = minX + y * width;
                    int sourceRow = sourceY * texture.Width;
                    for (int x = minX; x < maxX; x++, destinationOffset++)
                    {
                        int sourceX = sourceXs[x - minX];
                        if (sourceX < 0 || sourceX >= texture.Width)
                            continue;

                        Rgba32 sourcePixel = texture.Pixels[sourceX + sourceRow];
                        canvas[destinationOffset] = identityColor
                            ? BlendAdjusted(canvas[destinationOffset], sourcePixel, blend)
                            : Blend(canvas[destinationOffset], sourcePixel, add, mult, hsl, blend);
                    }
                }
                return;
            }

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    AfpPoint source = inverse.MultiplyPoint(new AfpPoint(x + 0.5, y + 0.5));
                    int sourceX = (int)source.X;
                    int sourceY = (int)source.Y;
                    if (sourceX < 0 || sourceY < 0 || sourceX >= texture.Width || sourceY >= texture.Height) continue;
                    int destinationOffset = x + y * width;
                    Rgba32 sourcePixel = texture.Pixels[sourceX + sourceY * texture.Width];
                    canvas[destinationOffset] = identityColor
                        ? BlendAdjusted(canvas[destinationOffset], sourcePixel, blend)
                        : Blend(canvas[destinationOffset], sourcePixel, add, mult, hsl, blend);
                }
            }
        }

        private static Rgba32 Blend(Rgba32 destination, Rgba32 source, AfpColor add, AfpColor mult, AfpHsl hsl, int mode)
        {
            double r = Clamp(source.R * mult.R + 255.0 * add.R);
            double g = Clamp(source.G * mult.G + 255.0 * add.G);
            double b = Clamp(source.B * mult.B + 255.0 * add.B);
            double a = Clamp(source.A * mult.A + 255.0 * add.A);
            if (!hsl.IsIdentity)
                ShiftHsl(ref r, ref g, ref b, hsl);
            return BlendAdjusted(destination, new Rgba32((byte)r, (byte)g, (byte)b, (byte)a), mode);
        }

        private static Rgba32 BlendAdjusted(Rgba32 destination, Rgba32 adjusted, int mode)
        {
            if (mode == 3)
            {
                double alpha = adjusted.A / 255.0;
                return new Rgba32(
                    (byte)Clamp(255.0 * (destination.R / 255.0) * (adjusted.R / 255.0) * alpha + destination.R * (1.0 - alpha)),
                    (byte)Clamp(255.0 * (destination.G / 255.0) * (adjusted.G / 255.0) * alpha + destination.G * (1.0 - alpha)),
                    (byte)Clamp(255.0 * (destination.B / 255.0) * (adjusted.B / 255.0) * alpha + destination.B * (1.0 - alpha)),
                    destination.A);
            }
            if (mode == 8)
            {
                double alpha = adjusted.A / 255.0;
                return new Rgba32((byte)Clamp(destination.R + adjusted.R * alpha),
                    (byte)Clamp(destination.G + adjusted.G * alpha),
                    (byte)Clamp(destination.B + adjusted.B * alpha),
                    (byte)Clamp(destination.A + 255.0 * alpha));
            }
            if (mode == 9 || mode == 70)
            {
                double alpha = adjusted.A / 255.0;
                return new Rgba32((byte)Clamp(destination.R - adjusted.R * alpha),
                    (byte)Clamp(destination.G - adjusted.G * alpha),
                    (byte)Clamp(destination.B - adjusted.B * alpha), destination.A);
            }
            if (mode == 13)
            {
                return new Rgba32((byte)Clamp(510.0 * destination.R / 255.0 * adjusted.R / 255.0),
                    (byte)Clamp(510.0 * destination.G / 255.0 * adjusted.G / 255.0),
                    (byte)Clamp(510.0 * destination.B / 255.0 * adjusted.B / 255.0), destination.A);
            }

            if (adjusted.A == 0) return destination;
            if (adjusted.A == 255) return adjusted;
            double sourceAlpha = adjusted.A / 255.0;
            double destinationAlpha = destination.A / 255.0;
            double remainder = 1.0 - sourceAlpha;
            double outputAlpha = Math.Min(1.0, sourceAlpha + destinationAlpha * remainder);
            if (outputAlpha <= 0.0) return default;
            return new Rgba32(
                (byte)Clamp((destination.R * destinationAlpha * remainder + adjusted.R * sourceAlpha) / outputAlpha),
                (byte)Clamp((destination.G * destinationAlpha * remainder + adjusted.G * sourceAlpha) / outputAlpha),
                (byte)Clamp((destination.B * destinationAlpha * remainder + adjusted.B * sourceAlpha) / outputAlpha),
                (byte)Clamp(255.0 * outputAlpha));
        }

        private static void ShiftHsl(ref double r, ref double g, ref double b, AfpHsl shift)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Math.Max(rf, Math.Max(gf, bf));
            double min = Math.Min(rf, Math.Min(gf, bf));
            double h = 0.0, s = 0.0, l = (max + min) / 2.0;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == rf) h = (gf - bf) / d + (gf < bf ? 6.0 : 0.0);
                else if (max == gf) h = (bf - rf) / d + 2.0;
                else h = (rf - gf) / d + 4.0;
                h /= 6.0;
            }
            h = (h + shift.H) % 1.0; if (h < 0) h += 1.0;
            s = Math.Clamp(s + shift.S, 0.0, 1.0);
            l = Math.Clamp(l + shift.L, 0.0, 1.0);
            if (s == 0.0) { r = g = b = Clamp(l * 255.0); return; }
            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            r = Clamp(HueToRgb(p, q, h + 1.0 / 3.0) * 255.0);
            g = Clamp(HueToRgb(p, q, h) * 255.0);
            b = Clamp(HueToRgb(p, q, h - 1.0 / 3.0) * 255.0);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        private static Rgba32 ToPixel(AfpColor color) => new Rgba32(
            (byte)Clamp(color.R * 255.0), (byte)Clamp(color.G * 255.0),
            (byte)Clamp(color.B * 255.0), (byte)Clamp(color.A * 255.0));

        private static double Clamp(double value) => Math.Min(255.0, Math.Max(0.0, Math.Round(value)));
    }
}
