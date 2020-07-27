﻿/*
    VectSharp - A light library for C# vector graphics.
    Copyright (C) 2020  Giorgio Bianchini
 
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, version 3.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace VectSharp.SVG
{
    /// <summary>
    /// Contains methods to read an SVG image file.
    /// </summary>
    public static class Parser
    {
        /// <summary>
        /// Parses SVG source into a <see cref="Page"/> containing the image represented by the code.
        /// </summary>
        /// <param name="svgSource">The SVG source code.</param>
        /// <returns>A <see cref="Page"/> containing the image represented by the <paramref name="svgSource"/>.</returns>
        public static Page FromString(string svgSource)
        {
            XmlDocument svgDoc = new XmlDocument();
            svgDoc.LoadXml(svgSource);

            Dictionary<string, FontFamily> embeddedFonts = new Dictionary<string, FontFamily>();

            foreach (XmlNode styleNode in svgDoc.GetElementsByTagName("style"))
            {
                foreach (KeyValuePair<string, FontFamily> fnt in GetEmbeddedFonts(styleNode.InnerText))
                {
                    embeddedFonts.Add(fnt.Key, fnt.Value);
                }
            }

            Graphics gpr = new Graphics();

            Size pageSize = InterpretSVGObject(svgDoc.GetElementsByTagName("svg")[0], gpr, new PresentationAttributes() { EmbeddedFonts = embeddedFonts });

            Page pg = new Page(pageSize.Width, pageSize.Height);

            pg.Graphics = gpr;

            return pg;
        }

        /// <summary>
        /// Parses an SVG image file into a <see cref="Page"/> containing the image.
        /// </summary>
        /// <param name="fileName">The path to the SVG image file.</param>
        /// <returns>A <see cref="Page"/> containing the image represented by the file.</returns>
        public static Page FromFile(string fileName)
        {
            return FromString(File.ReadAllText(fileName));
        }

        /// <summary>
        /// Parses an stream containing SVG source code into a <see cref="Page"/> containing the image represented by the code.
        /// </summary>
        /// <param name="svgSourceStream">The stream containing SVG source code.</param>
        /// <returns>A <see cref="Page"/> containing the image represented by the <paramref name="svgSourceStream"/>.</returns>
        public static Page FromStream(Stream svgSourceStream)
        {
            using (StreamReader sr = new StreamReader(svgSourceStream))
            {
                return FromString(sr.ReadToEnd());
            }
        }

        private static Size InterpretSVGObject(XmlNode svgObject, Graphics gpr, PresentationAttributes attributes)
        {
            double[] viewBox = ParseListOfDoubles(svgObject.Attributes?["viewBox"]?.Value);

            double width, height, x, y;

            string widthAttribute = svgObject.Attributes?["width"]?.Value?.Replace("px", "");

            if (!double.TryParse(widthAttribute, out width)) { width = double.NaN; }

            string heightAttribute = svgObject.Attributes?["height"]?.Value?.Replace("px", "");
            if (!double.TryParse(heightAttribute, out height)) { height = double.NaN; }

            string xAttribute = svgObject.Attributes?["x"]?.Value;
            double.TryParse(xAttribute, out x);

            string yAttribute = svgObject.Attributes?["y"]?.Value;
            double.TryParse(yAttribute, out y);

            double scaleX = 1;
            double scaleY = 1;

            double postTranslateX = 0;
            double postTranslateY = 0;

            if (viewBox != null)
            {
                if (!double.IsNaN(width) && !double.IsNaN(height))
                {
                    scaleX = width / viewBox[2];
                    scaleY = height / viewBox[3];
                }
                else if (!double.IsNaN(width) && double.IsNaN(height))
                {
                    scaleX = width / viewBox[2];
                    scaleY = scaleX;
                }
                else if (double.IsNaN(width) && !double.IsNaN(height))
                {
                    scaleY = height / viewBox[3];
                    scaleX = scaleY;
                }

                postTranslateX = -viewBox[0];
                postTranslateY = -viewBox[1];
            }
            else
            {
                viewBox = new double[4];

                if (!double.IsNaN(width))
                {
                    viewBox[2] = width;
                }

                if (!double.IsNaN(height))
                {
                    viewBox[3] = height;
                }
            }

            double diagonal = Math.Sqrt(viewBox[2] * viewBox[2] + viewBox[3] * viewBox[3]) / Math.Sqrt(2);

            Size tbrSize = new Size(viewBox[2], viewBox[3]);

            gpr.Save();
            gpr.Translate(x, y);
            gpr.Scale(scaleX, scaleY);
            gpr.Translate(postTranslateX, postTranslateY);

            attributes = InterpretPresentationAttributes(svgObject, attributes, viewBox[2], viewBox[3], diagonal, gpr);

            InterpretSVGChildren(svgObject, gpr, attributes, viewBox[2], viewBox[3], diagonal);

            gpr.Restore();

            return tbrSize;
        }

        private static void InterpretSVGChildren(XmlNode svgObject, Graphics gpr, PresentationAttributes attributes, double width, double height, double diagonal)
        {
            foreach (XmlNode child in svgObject.ChildNodes)
            {
                InterpretSVGElement(child, gpr, attributes, width, height, diagonal);
            }
        }

        private static void InterpretSVGElement(XmlNode currObject, Graphics gpr, PresentationAttributes attributes, double width, double height, double diagonal)
        {
            if (currObject.NodeType == XmlNodeType.EntityReference)
            {
                InterpretSVGChildren(currObject, gpr, attributes, width, height, diagonal);
            }
            else if (currObject.Name.Equals("svg", StringComparison.OrdinalIgnoreCase))
            {
                InterpretSVGObject(currObject, gpr, attributes);
            }
            else if (currObject.Name.Equals("line", StringComparison.OrdinalIgnoreCase))
            {
                InterpretLineObject(currObject, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("circle", StringComparison.OrdinalIgnoreCase))
            {
                InterpretCircleObject(currObject, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("ellipse", StringComparison.OrdinalIgnoreCase))
            {
                InterpretEllipseObject(currObject, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                InterpretPathObject(currObject, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("polyline", StringComparison.OrdinalIgnoreCase))
            {
                InterpretPolyLineObject(currObject, false, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("polygon", StringComparison.OrdinalIgnoreCase))
            {
                InterpretPolyLineObject(currObject, true, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("rect", StringComparison.OrdinalIgnoreCase))
            {
                InterpretRectObject(currObject, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("use", StringComparison.OrdinalIgnoreCase))
            {
                InterpretUseObject(currObject, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("g", StringComparison.OrdinalIgnoreCase))
            {
                InterpretGObject(currObject, gpr, width, height, diagonal, attributes);
            }
            else if (currObject.Name.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                InterpretTextObject(currObject, gpr, width, height, diagonal, attributes);
            }
        }

        private static void InterpretTextObject(XmlNode currObject, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes, double x = 0, double y = 0, double fontSize = double.NaN, string fontFamily = null, string textAlign = null)
        {
            PresentationAttributes currAttributes = InterpretPresentationAttributes(currObject, attributes, width, height, diagonal, gpr);

            x = ParseLengthOrPercentage(currObject.Attributes?["x"]?.Value, width, x);
            y = ParseLengthOrPercentage(currObject.Attributes?["y"]?.Value, height, y);

            fontFamily = currObject.Attributes?["font-family"]?.Value ?? fontFamily;
            fontSize = ParseLengthOrPercentage(currObject.Attributes?["font-size"]?.Value, width, fontSize);
            textAlign = currObject.Attributes?["text-align"]?.Value ?? textAlign;

            if (currObject.ChildNodes.OfType<XmlNode>().Any(a => a.NodeType != XmlNodeType.Text))
            {
                foreach (XmlNode child in currObject.ChildNodes)
                {
                    InterpretTextObject(child, gpr, width, height, diagonal, currAttributes, x, y, fontSize, fontFamily, textAlign);
                }
            }
            else
            {
                string text = currObject.InnerText;

                if (!double.IsNaN(fontSize) && !string.IsNullOrEmpty(text))
                {
                    FontFamily parsedFontFamily = ParseFontFamily(fontFamily, currAttributes.EmbeddedFonts);
                    string fontWeight = currObject.Attributes?["font-weight"]?.Value;
                    string fontStyle = currObject.Attributes?["font-style"]?.Value;

                    if (fontWeight != null && (fontWeight.Equals("bold", StringComparison.OrdinalIgnoreCase) || fontWeight.Equals("bolder", StringComparison.OrdinalIgnoreCase) || (int.TryParse(fontWeight, out int weight) && weight >= 500)))
                    {
                        parsedFontFamily = GetBoldFontFamily(parsedFontFamily);
                    }

                    if (fontStyle != null && (fontStyle.Equals("italic", StringComparison.OrdinalIgnoreCase) || fontStyle.Equals("oblique", StringComparison.OrdinalIgnoreCase)))
                    {
                        parsedFontFamily = GetItalicFontFamily(parsedFontFamily);
                    }

                    Font fnt = new Font(parsedFontFamily, fontSize);

                    if (fnt.FontFamily.TrueTypeFile != null)
                    {
                        Font.DetailedFontMetrics metrics = fnt.MeasureTextAdvanced(text);
                        x += metrics.LeftSideBearing;

                        if (!string.IsNullOrEmpty(textAlign) && (textAlign.Equals("right", StringComparison.OrdinalIgnoreCase) || textAlign.Equals("end", StringComparison.OrdinalIgnoreCase)))
                        {
                            x -= metrics.Width + metrics.LeftSideBearing;
                        }
                        else if (!string.IsNullOrEmpty(textAlign) && textAlign.Equals("center", StringComparison.OrdinalIgnoreCase))
                        {
                            x -= metrics.Width * 0.5;
                        }

                    }

                    TextBaselines baseline = TextBaselines.Baseline;

                    string textBaseline = currObject.Attributes?["alignment-baseline"]?.Value;

                    if (textBaseline != null)
                    {
                        if (textBaseline.Equals("text-bottom", StringComparison.OrdinalIgnoreCase) || textBaseline.Equals("bottom", StringComparison.OrdinalIgnoreCase))
                        {
                            baseline = TextBaselines.Bottom;
                        }
                        if (textBaseline.Equals("middle", StringComparison.OrdinalIgnoreCase) || textBaseline.Equals("central", StringComparison.OrdinalIgnoreCase) || textBaseline.Equals("center", StringComparison.OrdinalIgnoreCase))
                        {
                            baseline = TextBaselines.Middle;
                        }
                        if (textBaseline.Equals("text-top", StringComparison.OrdinalIgnoreCase) || textBaseline.Equals("top", StringComparison.OrdinalIgnoreCase) || textBaseline.Equals("hanging", StringComparison.OrdinalIgnoreCase))
                        {
                            baseline = TextBaselines.Top;
                        }
                    }

                    if (currAttributes.StrokeFirst)
                    {
                        if (currAttributes.Stroke != null)
                        {
                            Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                            gpr.StrokeText(x, y, text, fnt, strokeColour, baseline, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                        }

                        if (currAttributes.Fill != null)
                        {
                            Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                            gpr.FillText(x, y, text, fnt, fillColour, baseline);
                        }
                    }
                    else
                    {
                        if (currAttributes.Fill != null)
                        {
                            Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                            gpr.FillText(x, y, text, fnt, fillColour, baseline);
                        }

                        if (currAttributes.Stroke != null)
                        {
                            Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                            gpr.StrokeText(x, y, text, fnt, strokeColour, baseline, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                        }
                    }
                }

                if (currAttributes.NeedsRestore)
                {
                    gpr.Restore();
                }
            }
        }

        private static FontFamily GetBoldFontFamily(FontFamily fontFamily)
        {
            switch (fontFamily.FileName)
            {
                case "Times-Roman":
                case "Times-Bold":
                    return new FontFamily(FontFamily.StandardFontFamilies.TimesBold);
                case "Times-Italic":
                case "Times-BoldItalic":
                    return new FontFamily(FontFamily.StandardFontFamilies.TimesBoldItalic);
                case "Helvetica":
                case "Helvetica-Bold":
                    return new FontFamily(FontFamily.StandardFontFamilies.HelveticaBold);
                case "Helvetica-Oblique":
                case "Helvetica-BoldOblique":
                    return new FontFamily(FontFamily.StandardFontFamilies.HelveticaBoldOblique);
                case "Courier":
                case "Courier-Bold":
                    return new FontFamily(FontFamily.StandardFontFamilies.CourierBold);
                case "Courier-Oblique":
                case "Courier-BoldOblique":
                    return new FontFamily(FontFamily.StandardFontFamilies.CourierBoldOblique);
                default:
                    return fontFamily;
            }
        }

        private static FontFamily GetItalicFontFamily(FontFamily fontFamily)
        {
            switch (fontFamily.FileName)
            {
                case "Times-Roman":
                case "Times-Italic":
                    return new FontFamily(FontFamily.StandardFontFamilies.TimesItalic);
                case "Times-Bold":
                case "Times-BoldItalic":
                    return new FontFamily(FontFamily.StandardFontFamilies.TimesBoldItalic); 
                case "Helvetica":
                case "Helvetica-Oblique":
                    return new FontFamily(FontFamily.StandardFontFamilies.HelveticaOblique);
                case "Helvetica-Bold":
                case "Helvetica-BoldOblique":
                    return new FontFamily(FontFamily.StandardFontFamilies.HelveticaBoldOblique);
                case "Courier":
                case "Courier-Oblique":
                    return new FontFamily(FontFamily.StandardFontFamilies.CourierOblique);
                case "Courier-Bold":
                case "Courier-BoldOblique":
                    return new FontFamily(FontFamily.StandardFontFamilies.CourierBoldOblique);
                default:
                    return fontFamily;
            }
        }

        private static FontFamily ParseFontFamily(string fontFamily, Dictionary<string, FontFamily> embeddedFonts)
        {
            string[] fontFamilies = Regexes.FontFamilySeparator.Split(fontFamily);

            foreach (string fam in fontFamilies)
            {
                string family = fam.Trim().Trim(',', '"').Trim();

                if (embeddedFonts.TryGetValue(family, out FontFamily tbr))
                {
                    return tbr;
                }

                List<(string, int)> matchedFamilies = new List<(string, int)>();

                for (int i = 0; i < FontFamily.StandardFamilies.Length; i++)
                {
                    if (family.StartsWith(FontFamily.StandardFamilies[i]))
                    {
                        matchedFamilies.Add((FontFamily.StandardFamilies[i], FontFamily.StandardFamilies[i].Length));
                    }
                }

                if (matchedFamilies.Count > 0)
                {
                    return new FontFamily((from el in matchedFamilies orderby el.Item2 descending select el.Item1).First());
                }
                else
                {
                    if (family.Equals("serif", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.TimesRoman);
                    }
                    else if (family.Equals("sans-serif", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.Helvetica);
                    }
                    else if (family.Equals("monospace", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.Courier);
                    }
                    else if (family.Equals("cursive", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.TimesItalic);
                    }
                    else if (family.Equals("system-ui", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.Helvetica);
                    }
                    else if (family.Equals("ui-serif", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.TimesRoman);
                    }
                    else if (family.Equals("ui-sans-serif", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.Helvetica);
                    }
                    else if (family.Equals("ui-monospace", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.Courier);
                    }
                    else if (family.Equals("StandardSymbolsPS", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.Symbol);
                    }
                    else if (family.Equals("D050000L", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FontFamily(FontFamily.StandardFontFamilies.ZapfDingbats);
                    }
                }
            }

            return new FontFamily(FontFamily.StandardFontFamilies.Helvetica);
        }

        private static void InterpretGObject(XmlNode currObject, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes)
        {
            PresentationAttributes currAttributes = InterpretPresentationAttributes(currObject, attributes, width, height, diagonal, gpr);

            InterpretSVGChildren(currObject, gpr, currAttributes, width, height, diagonal);

            if (currAttributes.NeedsRestore)
            {
                gpr.Restore();
            }
        }

        private static void InterpretUseObject(XmlNode currObject, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes)
        {
            double x, y, w, h;

            x = ParseLengthOrPercentage(currObject.Attributes?["x"]?.Value, width);
            y = ParseLengthOrPercentage(currObject.Attributes?["y"]?.Value, height);
            w = ParseLengthOrPercentage(currObject.Attributes?["width"]?.Value, width, double.NaN);
            h = ParseLengthOrPercentage(currObject.Attributes?["height"]?.Value, height, double.NaN);

            string id = currObject.Attributes?["href"]?.Value ?? currObject.Attributes?["xlink:href"]?.Value;

            if (id != null && id.StartsWith("#"))
            {
                id = id.Substring(1);

                XmlNode element = currObject.OwnerDocument.SelectSingleNode(string.Format("//*[@id='{0}']", id));

                if (element != null)
                {
                    XmlNode clone = element.Clone();

                    currObject.AppendChild(clone);


                    PresentationAttributes currAttributes = InterpretPresentationAttributes(currObject, attributes, width, height, diagonal, gpr);


                    gpr.Save();
                    gpr.Translate(x, y);

                    ((XmlElement)clone).SetAttribute("x", "0");
                    ((XmlElement)clone).SetAttribute("y", "0");

                    if (clone.Attributes?["viewBox"] != null)
                    {
                        ((XmlElement)clone).SetAttribute("width", w.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        ((XmlElement)clone).SetAttribute("height", h.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    InterpretSVGElement(clone, gpr, currAttributes, width, height, diagonal);

                    gpr.Restore();

                    if (currAttributes.NeedsRestore)
                    {
                        gpr.Restore();
                    }
                }
            }
        }

        private static void InterpretRectObject(XmlNode currObject, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes)
        {
            double x, y, w, h, rx, ry;

            x = ParseLengthOrPercentage(currObject.Attributes?["x"]?.Value, width);
            y = ParseLengthOrPercentage(currObject.Attributes?["y"]?.Value, height);
            w = ParseLengthOrPercentage(currObject.Attributes?["width"]?.Value, width);
            h = ParseLengthOrPercentage(currObject.Attributes?["height"]?.Value, height);
            rx = ParseLengthOrPercentage(currObject.Attributes?["rx"]?.Value, width, double.NaN);
            ry = ParseLengthOrPercentage(currObject.Attributes?["ry"]?.Value, height, double.NaN);

            if (w > 0 && h > 0)
            {
                if (double.IsNaN(rx) && !double.IsNaN(ry))
                {
                    rx = ry;
                }
                else if (!double.IsNaN(rx) && double.IsNaN(ry))
                {
                    ry = rx;
                }

                if (double.IsNaN(rx))
                {
                    rx = 0;
                }

                if (double.IsNaN(ry))
                {
                    ry = 0;
                }

                rx = Math.Min(rx, w / 2);
                ry = Math.Min(ry, h / 2);

                GraphicsPath path = new GraphicsPath();

                path.MoveTo(x + rx, y);
                path.LineTo(x + w - rx, y);

                if (rx > 0 && ry > 0)
                {
                    path.EllipticalArc(rx, ry, 0, false, true, new Point(x + w, y + ry));
                }

                path.LineTo(x + w, y + h - ry);

                if (rx > 0 && ry > 0)
                {
                    path.EllipticalArc(rx, ry, 0, false, true, new Point(x + w - rx, y + h));
                }

                path.LineTo(x + rx, y + h);

                if (rx > 0 && ry > 0)
                {
                    path.EllipticalArc(rx, ry, 0, false, true, new Point(x, y + h - ry));
                }

                path.LineTo(x, y + ry);

                if (rx > 0 && ry > 0)
                {
                    path.EllipticalArc(rx, ry, 0, false, true, new Point(x + rx, y));
                }

                path.Close();

                PresentationAttributes currAttributes = InterpretPresentationAttributes(currObject, attributes, width, height, diagonal, gpr);

                if (currAttributes.StrokeFirst)
                {
                    if (currAttributes.Stroke != null)
                    {
                        Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                        gpr.StrokePath(path, strokeColour, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                    }

                    if (currAttributes.Fill != null)
                    {
                        Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                        gpr.FillPath(path, fillColour);
                    }
                }
                else
                {
                    if (currAttributes.Fill != null)
                    {
                        Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                        gpr.FillPath(path, fillColour);
                    }

                    if (currAttributes.Stroke != null)
                    {
                        Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                        gpr.StrokePath(path, strokeColour, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                    }
                }

                if (currAttributes.NeedsRestore)
                {
                    gpr.Restore();
                }
            }
        }

        private static void InterpretPolyLineObject(XmlNode currObject, bool isPolygon, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes)
        {
            string points = currObject.Attributes?["points"]?.Value;

            if (points != null)
            {
                double[] coordinates = ParseListOfDoubles(points);

                GraphicsPath path = new GraphicsPath();

                for (int i = 0; i < coordinates.Length; i += 2)
                {
                    path.LineTo(coordinates[i], coordinates[i + 1]);
                }

                if (isPolygon)
                {
                    path.Close();
                }

                PresentationAttributes currAttributes = InterpretPresentationAttributes(currObject, attributes, width, height, diagonal, gpr);

                if (currAttributes.StrokeFirst)
                {
                    if (currAttributes.Stroke != null)
                    {
                        Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                        gpr.StrokePath(path, strokeColour, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                    }

                    if (currAttributes.Fill != null)
                    {
                        Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                        gpr.FillPath(path, fillColour);
                    }
                }
                else
                {
                    if (currAttributes.Fill != null)
                    {
                        Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                        gpr.FillPath(path, fillColour);
                    }

                    if (currAttributes.Stroke != null)
                    {
                        Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                        gpr.StrokePath(path, strokeColour, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                    }
                }

                if (currAttributes.NeedsRestore)
                {
                    gpr.Restore();
                }
            }
        }

        private static void InterpretPathObject(XmlNode currObject, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes)
        {
            string d = currObject.Attributes?["d"]?.Value;

            if (d != null)
            {
                List<string> pathData = TokenisePathData(d);

                GraphicsPath path = new GraphicsPath();

                Point lastPoint = new Point();
                Point? figureStartPoint = null;

                char lastCommand = '\0';
                Point lastCtrlPoint = new Point();

                for (int i = 0; i < pathData.Count; i++)
                {
                    Point delta = new Point();

                    bool isAbsolute = char.IsUpper(pathData[i][0]);

                    if (!isAbsolute)
                    {
                        delta = lastPoint;
                    }

                    switch (pathData[i][0])
                    {
                        case 'M':
                        case 'm':
                            lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                            path.MoveTo(lastPoint);
                            figureStartPoint = lastPoint;
                            i += 2;
                            lastCommand = 'M';
                            while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                            {
                                if (!isAbsolute)
                                {
                                    delta = lastPoint;
                                }
                                else
                                {
                                    delta = new Point();
                                }

                                lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                path.LineTo(lastPoint);

                                i += 2;
                                lastCommand = 'L';
                            }
                            break;
                        case 'L':
                        case 'l':
                            lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                            path.LineTo(lastPoint);
                            if (figureStartPoint == null)
                            {
                                figureStartPoint = lastPoint;
                            }
                            i += 2;
                            lastCommand = 'L';
                            while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                            {
                                if (!isAbsolute)
                                {
                                    delta = lastPoint;
                                }
                                else
                                {
                                    delta = new Point();
                                }

                                lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                path.LineTo(lastPoint);

                                i += 2;
                            }
                            break;
                        case 'H':
                        case 'h':
                            lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), lastPoint.Y);
                            path.LineTo(lastPoint);
                            if (figureStartPoint == null)
                            {
                                figureStartPoint = lastPoint;
                            }
                            i++;
                            lastCommand = 'L';
                            while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                            {
                                if (!isAbsolute)
                                {
                                    delta = lastPoint;
                                }
                                else
                                {
                                    delta = new Point();
                                }

                                lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), lastPoint.Y);
                                path.LineTo(lastPoint);

                                i++;
                            }
                            break;
                        case 'V':
                        case 'v':
                            lastPoint = new Point(lastPoint.X, delta.Y + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture));
                            path.LineTo(lastPoint);
                            if (figureStartPoint == null)
                            {
                                figureStartPoint = lastPoint;
                            }
                            i++;
                            lastCommand = 'L';
                            while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                            {
                                if (!isAbsolute)
                                {
                                    delta = lastPoint;
                                }
                                else
                                {
                                    delta = new Point();
                                }

                                lastPoint = new Point(lastPoint.X, delta.Y + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture));
                                path.LineTo(lastPoint);

                                i++;
                            }
                            break;
                        case 'C':
                        case 'c':
                            {
                                Point ctrlPoint1 = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                i += 2;

                                Point ctrlPoint2 = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                i += 2;

                                lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                i += 2;

                                if (figureStartPoint == null)
                                {
                                    figureStartPoint = lastPoint;
                                }

                                path.CubicBezierTo(ctrlPoint1, ctrlPoint2, lastPoint);

                                lastCtrlPoint = ctrlPoint2;
                                lastCommand = 'C';

                                while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                                {
                                    if (!isAbsolute)
                                    {
                                        delta = lastPoint;
                                    }
                                    else
                                    {
                                        delta = new Point();
                                    }

                                    ctrlPoint1 = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 2;

                                    ctrlPoint2 = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 2;

                                    lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 2;

                                    path.CubicBezierTo(ctrlPoint1, ctrlPoint2, lastPoint);
                                }
                            }
                            break;
                        case 'S':
                        case 's':
                            {
                                Point ctrlPoint1;

                                if (lastCommand == 'C')
                                {
                                    ctrlPoint1 = new Point(2 * lastPoint.X - lastCtrlPoint.X, 2 * lastPoint.Y - lastCtrlPoint.Y);
                                }
                                else
                                {
                                    ctrlPoint1 = lastPoint;
                                }

                                Point ctrlPoint2 = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                i += 2;

                                lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                i += 2;

                                if (figureStartPoint == null)
                                {
                                    figureStartPoint = lastPoint;
                                }

                                path.CubicBezierTo(ctrlPoint1, ctrlPoint2, lastPoint);

                                lastCtrlPoint = ctrlPoint2;
                                lastCommand = 'C';

                                while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                                {
                                    if (!isAbsolute)
                                    {
                                        delta = lastPoint;
                                    }
                                    else
                                    {
                                        delta = new Point();
                                    }

                                    ctrlPoint1 = new Point(2 * lastPoint.X - lastCtrlPoint.X, 2 * lastPoint.Y - lastCtrlPoint.Y);

                                    ctrlPoint2 = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 2;

                                    lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 2;

                                    path.CubicBezierTo(ctrlPoint1, ctrlPoint2, lastPoint);

                                    lastCtrlPoint = ctrlPoint2;
                                }
                            }
                            break;
                        case 'Q':
                        case 'q':
                            {
                                Point ctrlPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                i += 2;

                                Point actualCP1 = new Point(lastPoint.X + 2 * (ctrlPoint.X - lastPoint.X) / 3, lastPoint.Y + 2 * (ctrlPoint.Y - lastPoint.Y) / 3);

                                lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                i += 2;

                                Point actualCP2 = new Point(lastPoint.X + 2 * (ctrlPoint.X - lastPoint.X) / 3, lastPoint.Y + 2 * (ctrlPoint.Y - lastPoint.Y) / 3);

                                if (figureStartPoint == null)
                                {
                                    figureStartPoint = lastPoint;
                                }

                                path.CubicBezierTo(actualCP1, actualCP2, lastPoint);

                                lastCtrlPoint = ctrlPoint;
                                lastCommand = 'Q';

                                while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                                {
                                    if (!isAbsolute)
                                    {
                                        delta = lastPoint;
                                    }
                                    else
                                    {
                                        delta = new Point();
                                    }

                                    ctrlPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 2;

                                    actualCP1 = new Point(lastPoint.X + 2 * (ctrlPoint.X - lastPoint.X) / 3, lastPoint.Y + 2 * (ctrlPoint.Y - lastPoint.Y) / 3);

                                    lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 2;

                                    actualCP2 = new Point(lastPoint.X + 2 * (ctrlPoint.X - lastPoint.X) / 3, lastPoint.Y + 2 * (ctrlPoint.Y - lastPoint.Y) / 3);

                                    path.CubicBezierTo(actualCP1, actualCP2, lastPoint);
                                    lastCtrlPoint = ctrlPoint;
                                }


                            }
                            break;
                        case 'T':
                        case 't':
                            {
                                Point ctrlPoint;

                                if (lastCommand == 'Q')
                                {
                                    ctrlPoint = new Point(2 * lastPoint.X - lastCtrlPoint.X, 2 * lastPoint.Y - lastCtrlPoint.Y);
                                }
                                else
                                {
                                    ctrlPoint = lastPoint;
                                }

                                Point actualCP1 = new Point(lastPoint.X + 2 * (ctrlPoint.X - lastPoint.X) / 3, lastPoint.Y + 2 * (ctrlPoint.Y - lastPoint.Y) / 3);

                                lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                i += 2;

                                Point actualCP2 = new Point(lastPoint.X + 2 * (ctrlPoint.X - lastPoint.X) / 3, lastPoint.Y + 2 * (ctrlPoint.Y - lastPoint.Y) / 3);

                                if (figureStartPoint == null)
                                {
                                    figureStartPoint = lastPoint;
                                }

                                path.CubicBezierTo(actualCP1, actualCP2, lastPoint);
                                lastCtrlPoint = ctrlPoint;
                                lastCommand = 'Q';

                                while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                                {
                                    if (!isAbsolute)
                                    {
                                        delta = lastPoint;
                                    }
                                    else
                                    {
                                        delta = new Point();
                                    }

                                    ctrlPoint = new Point(2 * lastPoint.X - lastCtrlPoint.X, 2 * lastPoint.Y - lastCtrlPoint.Y);

                                    actualCP1 = new Point(lastPoint.X + 2 * (ctrlPoint.X - lastPoint.X) / 3, lastPoint.Y + 2 * (ctrlPoint.Y - lastPoint.Y) / 3);

                                    lastPoint = new Point(delta.X + double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 2;

                                    actualCP2 = new Point(lastPoint.X + 2 * (ctrlPoint.X - lastPoint.X) / 3, lastPoint.Y + 2 * (ctrlPoint.Y - lastPoint.Y) / 3);

                                    path.CubicBezierTo(actualCP1, actualCP2, lastPoint);

                                    lastCtrlPoint = ctrlPoint;
                                }
                            }
                            break;
                        case 'A':
                        case 'a':
                            {
                                Point startPoint = lastPoint;

                                if (figureStartPoint == null)
                                {
                                    figureStartPoint = lastPoint;
                                }

                                Point radii = new Point(double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                double angle = double.Parse(pathData[i + 3], System.Globalization.CultureInfo.InvariantCulture) * Math.PI / 180;
                                bool largeArcFlag = pathData[i + 4][0] == '1';
                                bool sweepFlag = pathData[i + 5][0] == '1';

                                lastPoint = new Point(delta.X + double.Parse(pathData[i + 6], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 7], System.Globalization.CultureInfo.InvariantCulture));
                                i += 7;

                                path.EllipticalArc(radii.X, radii.Y, angle, largeArcFlag, sweepFlag, lastPoint);

                                while (i < pathData.Count - 1 && !char.IsLetter(pathData[i + 1][0]))
                                {
                                    if (!isAbsolute)
                                    {
                                        delta = lastPoint;
                                    }
                                    else
                                    {
                                        delta = new Point();
                                    }

                                    startPoint = lastPoint;
                                    radii = new Point(double.Parse(pathData[i + 1], System.Globalization.CultureInfo.InvariantCulture), double.Parse(pathData[i + 2], System.Globalization.CultureInfo.InvariantCulture));
                                    angle = double.Parse(pathData[i + 3], System.Globalization.CultureInfo.InvariantCulture) * Math.PI / 180;
                                    largeArcFlag = pathData[i + 4][0] == '1';
                                    sweepFlag = pathData[i + 5][0] == '1';

                                    lastPoint = new Point(delta.X + double.Parse(pathData[i + 6], System.Globalization.CultureInfo.InvariantCulture), delta.Y + double.Parse(pathData[i + 7], System.Globalization.CultureInfo.InvariantCulture));
                                    i += 7;

                                    path.EllipticalArc(radii.X, radii.Y, angle, largeArcFlag, sweepFlag, lastPoint);
                                }
                            }

                            break;
                        case 'Z':
                        case 'z':
                            path.Close();
                            lastPoint = figureStartPoint.Value;
                            figureStartPoint = null;
                            lastCommand = 'Z';
                            break;
                    }
                }

                PresentationAttributes currAttributes = InterpretPresentationAttributes(currObject, attributes, width, height, diagonal, gpr);

                if (currAttributes.StrokeFirst)
                {
                    if (currAttributes.Stroke != null)
                    {
                        Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                        gpr.StrokePath(path, strokeColour, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                    }

                    if (currAttributes.Fill != null)
                    {
                        Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                        gpr.FillPath(path, fillColour);
                    }
                }
                else
                {
                    if (currAttributes.Fill != null)
                    {
                        Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                        gpr.FillPath(path, fillColour);
                    }

                    if (currAttributes.Stroke != null)
                    {
                        Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                        gpr.StrokePath(path, strokeColour, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                    }
                }

                if (currAttributes.NeedsRestore)
                {
                    gpr.Restore();
                }
            }

        }

        private static List<string> TokenisePathData(string d)
        {
            List<string> tbr = new List<string>();

            string currToken = "";

            for (int i = 0; i < d.Length; i++)
            {
                char c = d[i];

                if (c >= '0' && c <= '9' || c == '.' || c == 'e')
                {
                    currToken += c;
                }
                else if (c == '-' || c == '+')
                {
                    if (i > 0 && d[i - 1] == 'e')
                    {
                        currToken += c;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(currToken))
                        {
                            tbr.Add(currToken);
                        }
                        currToken = "" + c;
                    }
                }
                else if (char.IsWhiteSpace(c) || c == ',')
                {
                    if (!string.IsNullOrEmpty(currToken))
                    {
                        tbr.Add(currToken);
                    }
                    currToken = "";
                }
                else if ("MmLlHhVvCcSsQqTtAaZz".Contains(c))
                {
                    if (!string.IsNullOrEmpty(currToken))
                    {
                        tbr.Add(currToken);
                    }
                    tbr.Add(c.ToString());
                    currToken = "";
                }
            }

            if (!string.IsNullOrEmpty(currToken))
            {
                tbr.Add(currToken);
            }

            return tbr;
        }

        private static void InterpretCircleObject(XmlNode circleObject, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes)
        {
            double cx, cy, r;

            cx = ParseLengthOrPercentage(circleObject.Attributes?["cx"]?.Value, width);
            cy = ParseLengthOrPercentage(circleObject.Attributes?["cy"]?.Value, height);
            r = ParseLengthOrPercentage(circleObject.Attributes?["r"]?.Value, diagonal);

            PresentationAttributes circleAttributes = InterpretPresentationAttributes(circleObject, attributes, width, height, diagonal, gpr);

            if (circleAttributes.StrokeFirst)
            {
                if (circleAttributes.Stroke != null)
                {
                    Colour strokeColour = Colour.FromRgba(circleAttributes.Stroke.Value.R, circleAttributes.Stroke.Value.G, circleAttributes.Stroke.Value.B, circleAttributes.Stroke.Value.A * circleAttributes.Opacity * circleAttributes.StrokeOpacity);
                    gpr.StrokePath(new GraphicsPath().Arc(cx, cy, r, 0, 2 * Math.PI).Close(), strokeColour, circleAttributes.StrokeThickness, circleAttributes.LineCap, circleAttributes.LineJoin, circleAttributes.LineDash);
                }

                if (circleAttributes.Fill != null)
                {
                    Colour fillColour = Colour.FromRgba(circleAttributes.Fill.Value.R, circleAttributes.Fill.Value.G, circleAttributes.Fill.Value.B, circleAttributes.Fill.Value.A * circleAttributes.Opacity * circleAttributes.FillOpacity);
                    gpr.FillPath(new GraphicsPath().Arc(cx, cy, r, 0, 2 * Math.PI).Close(), fillColour);
                }
            }
            else
            {
                if (circleAttributes.Fill != null)
                {
                    Colour fillColour = Colour.FromRgba(circleAttributes.Fill.Value.R, circleAttributes.Fill.Value.G, circleAttributes.Fill.Value.B, circleAttributes.Fill.Value.A * circleAttributes.Opacity * circleAttributes.FillOpacity);
                    gpr.FillPath(new GraphicsPath().Arc(cx, cy, r, 0, 2 * Math.PI).Close(), fillColour);
                }

                if (circleAttributes.Stroke != null)
                {
                    Colour strokeColour = Colour.FromRgba(circleAttributes.Stroke.Value.R, circleAttributes.Stroke.Value.G, circleAttributes.Stroke.Value.B, circleAttributes.Stroke.Value.A * circleAttributes.Opacity * circleAttributes.StrokeOpacity);
                    gpr.StrokePath(new GraphicsPath().Arc(cx, cy, r, 0, 2 * Math.PI).Close(), strokeColour, circleAttributes.StrokeThickness, circleAttributes.LineCap, circleAttributes.LineJoin, circleAttributes.LineDash);
                }
            }

            if (circleAttributes.NeedsRestore)
            {
                gpr.Restore();
            }
        }

        private static void InterpretEllipseObject(XmlNode currObject, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes)
        {
            double cx, cy, rx, ry;

            cx = ParseLengthOrPercentage(currObject.Attributes?["cx"]?.Value, width);
            cy = ParseLengthOrPercentage(currObject.Attributes?["cy"]?.Value, height);
            rx = ParseLengthOrPercentage(currObject.Attributes?["rx"]?.Value, width, double.NaN);
            ry = ParseLengthOrPercentage(currObject.Attributes?["ry"]?.Value, height, double.NaN);

            if (double.IsNaN(rx) && !double.IsNaN(ry))
            {
                rx = ry;
            }
            else if (!double.IsNaN(rx) && double.IsNaN(ry))
            {
                ry = rx;
            }

            if (rx > 0 && ry > 0)
            {

                PresentationAttributes currAttributes = InterpretPresentationAttributes(currObject, attributes, width, height, diagonal, gpr);

                double r = Math.Min(rx, ry);

                gpr.Save();
                gpr.Translate(cx, cy);
                gpr.Scale(rx / r, ry / r);

                if (currAttributes.StrokeFirst)
                {
                    if (currAttributes.Stroke != null)
                    {
                        Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                        gpr.StrokePath(new GraphicsPath().Arc(0, 0, r, 0, 2 * Math.PI).Close(), strokeColour, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                    }

                    if (currAttributes.Fill != null)
                    {
                        Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                        gpr.FillPath(new GraphicsPath().Arc(0, 0, r, 0, 2 * Math.PI).Close(), fillColour);
                    }
                }
                else
                {
                    if (currAttributes.Fill != null)
                    {
                        Colour fillColour = Colour.FromRgba(currAttributes.Fill.Value.R, currAttributes.Fill.Value.G, currAttributes.Fill.Value.B, currAttributes.Fill.Value.A * currAttributes.Opacity * currAttributes.FillOpacity);
                        gpr.FillPath(new GraphicsPath().Arc(0, 0, r, 0, 2 * Math.PI).Close(), fillColour);
                    }

                    if (currAttributes.Stroke != null)
                    {
                        Colour strokeColour = Colour.FromRgba(currAttributes.Stroke.Value.R, currAttributes.Stroke.Value.G, currAttributes.Stroke.Value.B, currAttributes.Stroke.Value.A * currAttributes.Opacity * currAttributes.StrokeOpacity);
                        gpr.StrokePath(new GraphicsPath().Arc(0, 0, r, 0, 2 * Math.PI).Close(), strokeColour, currAttributes.StrokeThickness, currAttributes.LineCap, currAttributes.LineJoin, currAttributes.LineDash);
                    }
                }

                gpr.Restore();

                if (currAttributes.NeedsRestore)
                {
                    gpr.Restore();
                }
            }
        }

        private static void InterpretLineObject(XmlNode lineObject, Graphics gpr, double width, double height, double diagonal, PresentationAttributes attributes)
        {
            double x1, x2, y1, y2;

            x1 = ParseLengthOrPercentage(lineObject.Attributes?["x1"]?.Value, width);
            y1 = ParseLengthOrPercentage(lineObject.Attributes?["y1"]?.Value, height);
            x2 = ParseLengthOrPercentage(lineObject.Attributes?["x2"]?.Value, width);
            y2 = ParseLengthOrPercentage(lineObject.Attributes?["y2"]?.Value, height);

            PresentationAttributes lineAttributes = InterpretPresentationAttributes(lineObject, attributes, width, height, diagonal, gpr);

            if (lineAttributes.Stroke != null)
            {
                Colour strokeColour = Colour.FromRgba(lineAttributes.Stroke.Value.R, lineAttributes.Stroke.Value.G, lineAttributes.Stroke.Value.B, lineAttributes.Stroke.Value.A * lineAttributes.Opacity * lineAttributes.StrokeOpacity);
                gpr.StrokePath(new GraphicsPath().MoveTo(x1, y1).LineTo(x2, y2), strokeColour, lineAttributes.StrokeThickness, lineAttributes.LineCap, lineAttributes.LineJoin, lineAttributes.LineDash);
            }

            if (lineAttributes.NeedsRestore)
            {
                gpr.Restore();
            }
        }

        private static void SetStyleAttributes(XmlNode obj)
        {
            string style = obj.Attributes?["style"]?.Value;

            if (!string.IsNullOrEmpty(style))
            {
                string[] splitStyle = style.Split(';');

                for (int i = 0; i < splitStyle.Length; i++)
                {
                    string[] styleCouple = splitStyle[i].Split(':');

                    if (styleCouple.Length == 2)
                    {
                        string styleName = styleCouple[0].Trim();
                        string styleValue = styleCouple[1].Trim();

                        ((XmlElement)obj).SetAttribute(styleName, styleValue);
                    }
                    else if (!string.IsNullOrWhiteSpace(splitStyle[i]))
                    {
                        throw new InvalidOperationException("The style specification is not valid: " + splitStyle[i]);
                    }
                }
            }
        }

        private static PresentationAttributes InterpretPresentationAttributes(XmlNode obj, PresentationAttributes parentPresentationAttributes, double width, double height, double diagonal, Graphics gpr)
        {
            SetStyleAttributes(obj);

            PresentationAttributes tbr = parentPresentationAttributes.Clone();

            string stroke = obj.Attributes?["stroke"]?.Value;
            string strokeOpacity = obj.Attributes?["stroke-opacity"]?.Value;
            string fill = obj.Attributes?["fill"]?.Value;
            string fillOpacity = obj.Attributes?["fill-opacity"]?.Value;
            string currentColour = obj.Attributes?["colour"]?.Value;
            string strokeThickness = obj.Attributes?["stroke-width"]?.Value;
            string lineCap = obj.Attributes?["stroke-linecap"]?.Value;
            string lineJoin = obj.Attributes?["stroke-linejoin"]?.Value;
            string opacity = obj.Attributes?["opacity"]?.Value;
            string strokeDashArray = obj.Attributes?["stroke-dasharray"]?.Value;
            string strokeDashOffset = obj.Attributes?["stroke-dashoffset"]?.Value;
            string paintOrder = obj.Attributes?["paint-order"]?.Value;

            string transform = obj.Attributes?["transform"]?.Value;

            if (stroke != null)
            {
                tbr.Stroke = Colour.FromCSSString(stroke);
            }

            if (strokeOpacity != null)
            {
                tbr.StrokeOpacity = ParseLengthOrPercentage(strokeOpacity, 1);
            }

            if (fill != null)
            {
                tbr.Fill = Colour.FromCSSString(fill);
            }

            if (fillOpacity != null)
            {
                tbr.FillOpacity = ParseLengthOrPercentage(fillOpacity, 1);
            }

            if (currentColour != null)
            {
                tbr.CurrentColour = Colour.FromCSSString(currentColour);
            }

            if (strokeThickness != null)
            {
                tbr.StrokeThickness = ParseLengthOrPercentage(strokeThickness, diagonal);
            }

            if (lineCap != null)
            {
                if (lineCap.Equals("butt", StringComparison.OrdinalIgnoreCase))
                {
                    tbr.LineCap = LineCaps.Butt;
                }
                else if (lineCap.Equals("round", StringComparison.OrdinalIgnoreCase))
                {
                    tbr.LineCap = LineCaps.Round;
                }
                else if (lineCap.Equals("square", StringComparison.OrdinalIgnoreCase))
                {
                    tbr.LineCap = LineCaps.Square;
                }
            }

            if (lineJoin != null)
            {
                if (lineJoin.Equals("bevel", StringComparison.OrdinalIgnoreCase))
                {
                    tbr.LineJoin = LineJoins.Bevel;
                }
                else if (lineJoin.Equals("miter", StringComparison.OrdinalIgnoreCase) || lineJoin.Equals("miter-clip", StringComparison.OrdinalIgnoreCase))
                {
                    tbr.LineJoin = LineJoins.Miter;
                }
                else if (lineJoin.Equals("round", StringComparison.OrdinalIgnoreCase))
                {
                    tbr.LineJoin = LineJoins.Round;
                }
            }

            if (opacity != null)
            {
                tbr.Opacity = ParseLengthOrPercentage(opacity, 1);
            }

            if (strokeDashArray != null)
            {
                if (strokeDashArray != "none")
                {
                    double[] parsedArray = ParseListOfDoubles(strokeDashArray);

                    tbr.LineDash = new LineDash(parsedArray[0], parsedArray.Length > 1 ? parsedArray[1] : parsedArray[0], tbr.LineDash.Phase);
                }
                else
                {
                    tbr.LineDash = LineDash.SolidLine;
                }
            }

            if (strokeDashOffset != null)
            {
                tbr.LineDash = new LineDash(tbr.LineDash.UnitsOn, tbr.LineDash.UnitsOff, ParseLengthOrPercentage(strokeDashOffset, diagonal));
            }

            if (paintOrder != null)
            {
                if (paintOrder.Equals("normal", StringComparison.OrdinalIgnoreCase))
                {
                    tbr.StrokeFirst = false;
                }
                else
                {
                    if (paintOrder.IndexOf("stroke", StringComparison.OrdinalIgnoreCase) >= 0 && (paintOrder.IndexOf("fill", StringComparison.OrdinalIgnoreCase) < 0 || paintOrder.IndexOf("fill", StringComparison.OrdinalIgnoreCase) > paintOrder.IndexOf("stroke", StringComparison.OrdinalIgnoreCase)))
                    {
                        tbr.StrokeFirst = true;
                    }
                    else
                    {
                        tbr.StrokeFirst = false;
                    }
                }
            }

            if (transform != null)
            {
                gpr.Save();
                tbr.NeedsRestore = true;

                string[] transforms = ParseListOfTransforms(transform);

                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].Equals("matrix", StringComparison.OrdinalIgnoreCase))
                    {
                        double a = ParseLengthOrPercentage(transforms[i + 1], 1);
                        double b = ParseLengthOrPercentage(transforms[i + 2], 1);
                        double c = ParseLengthOrPercentage(transforms[i + 3], 1);
                        double d = ParseLengthOrPercentage(transforms[i + 4], 1);
                        double e = ParseLengthOrPercentage(transforms[i + 5], 1);
                        double f = ParseLengthOrPercentage(transforms[i + 6], 1);

                        gpr.Transform(a, b, c, d, e, f);
                        i += 6;
                    }
                    else if (transforms[i].Equals("translate", StringComparison.OrdinalIgnoreCase))
                    {
                        double x = ParseLengthOrPercentage(transforms[i + 1], 1);

                        double y;

                        if (i < transforms.Length - 2 && !double.IsNaN(y = ParseLengthOrPercentage(transforms[i + 2], 1)))
                        {
                            gpr.Translate(x, y);
                            i += 2;
                        }
                        else
                        {
                            gpr.Translate(x, 0);
                            i++;
                        }
                    }
                    else if (transforms[i].Equals("scale", StringComparison.OrdinalIgnoreCase))
                    {
                        double x = ParseLengthOrPercentage(transforms[i + 1], 1);

                        double y;

                        if (i < transforms.Length - 2 && !double.IsNaN(y = ParseLengthOrPercentage(transforms[i + 2], 1)))
                        {
                            gpr.Scale(x, y);
                            i += 2;
                        }
                        else
                        {
                            gpr.Scale(x, x);
                            i++;
                        }
                    }
                    else if (transforms[i].Equals("rotate", StringComparison.OrdinalIgnoreCase))
                    {
                        double a = ParseLengthOrPercentage(transforms[i + 1], 1) * Math.PI / 180;

                        double x, y;

                        if (i < transforms.Length - 3 && !double.IsNaN(x = ParseLengthOrPercentage(transforms[i + 2], 1)) && !double.IsNaN(y = ParseLengthOrPercentage(transforms[i + 3], 1)))
                        {
                            gpr.RotateAt(a, new Point(x, y));
                            i += 2;
                        }
                        else
                        {
                            gpr.Rotate(a);
                            i++;
                        }
                    }
                    else if (transforms[i].Equals("skewX", StringComparison.OrdinalIgnoreCase))
                    {
                        double psi = ParseLengthOrPercentage(transforms[i + 1], 1) * Math.PI / 180;

                        gpr.Transform(1, 0, Math.Tan(psi), 1, 0, 0);

                        i++;
                    }
                    else if (transforms[i].Equals("skewY", StringComparison.OrdinalIgnoreCase))
                    {
                        double psi = ParseLengthOrPercentage(transforms[i + 1], 1) * Math.PI / 180;

                        gpr.Transform(1, Math.Tan(psi), 0, 1, 0, 0);

                        i++;
                    }
                }
            }

            return tbr;
        }

        private static double ParseLengthOrPercentage(string value, double total, double defaultValue = 0)
        {
            if (value != null)
            {
                if (value.Contains("%"))
                {
                    value = value.Replace("%", "");
                    return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) * total / 100;
                }
                else
                {
                    return double.Parse(value.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else
            {
                return defaultValue;
            }
        }

        private class PresentationAttributes
        {
            public Dictionary<string, FontFamily> EmbeddedFonts;

            public Colour? Stroke = null;
            public double StrokeOpacity = 1;
            public Colour? Fill = Colour.FromRgb(0, 0, 0);
            public double FillOpacity = 1;
            public Colour? CurrentColour = null;
            public double StrokeThickness = 1;
            public LineCaps LineCap = LineCaps.Butt;
            public LineJoins LineJoin = LineJoins.Miter;
            public double Opacity = 1;
            public LineDash LineDash = new LineDash(0, 0, 0);
            public bool NeedsRestore = false;
            public bool StrokeFirst = false;

            public PresentationAttributes Clone()
            {
                return new PresentationAttributes()
                {
                    EmbeddedFonts = this.EmbeddedFonts,

                    Stroke = this.Stroke,
                    StrokeOpacity = this.StrokeOpacity,
                    Fill = this.Fill,
                    FillOpacity = this.FillOpacity,
                    CurrentColour = this.CurrentColour,
                    StrokeThickness = this.StrokeThickness,
                    LineCap = this.LineCap,
                    LineJoin = this.LineJoin,
                    Opacity = this.Opacity,
                    LineDash = this.LineDash,
                    StrokeFirst = this.StrokeFirst
                };
            }
        }

        private static class Regexes
        {
            public static Regex ListSeparator = new Regex("[ \\t\\n\\r\\f]*,[ \\t\\n\\r\\f]*|[ \\t\\n\\r\\f]+", RegexOptions.Compiled);
            public static Regex FontFamilySeparator = new Regex("(?:^|,)(\"(?:[^\"])*\"|[^,]*)", RegexOptions.Compiled);
        }

        private static double[] ParseListOfDoubles(string value)
        {
            if (value == null)
            {
                return null;
            }

            string[] splitValue = Regexes.ListSeparator.Split(value);
            double[] tbr = new double[splitValue.Length];

            for (int i = 0; i < splitValue.Length; i++)
            {
                tbr[i] = double.Parse(splitValue[i]);
            }

            return tbr;
        }

        private static string[] ParseListOfTransforms(string value)
        {
            if (value == null)
            {
                return null;
            }

            string[] splitValue = Regexes.ListSeparator.Split(value.Replace("(", " ").Replace(")", " ").Trim());

            return splitValue;
        }

        private static List<KeyValuePair<string, FontFamily>> GetEmbeddedFonts(string styleBlock)
        {
            StringReader sr = new StringReader(styleBlock);

            List<KeyValuePair<string, FontFamily>> tbr = new List<KeyValuePair<string, FontFamily>>();

            while (sr.Peek() >= 0)
            {
                string token = ReadCSSToken(sr);

                if (token.Equals("@font-face", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> tokens = new List<string>();

                    while (!token.Equals("}", StringComparison.OrdinalIgnoreCase))
                    {
                        token = ReadCSSToken(sr);
                        tokens.Add(token);
                    }

                    KeyValuePair<string, FontFamily>? fontFace = ParseFontFaceBlock(tokens);

                    if (fontFace != null)
                    {
                        tbr.Add(fontFace.Value);
                    }
                }
            }

            return tbr;
        }

        private static KeyValuePair<string, FontFamily>? ParseFontFaceBlock(List<string> tokens)
        {
            int fontFamilyInd = tokens.IndexOf("font-family");
            string fontFamilyName = tokens[fontFamilyInd + 2].Trim().Trim('"').Trim();

            int srcInd = tokens.IndexOf("src");
            string src = tokens[srcInd + 2];

            string mimeType = src.Substring(src.IndexOf("data:") + 5);
            mimeType = mimeType.Substring(0, mimeType.IndexOf(";"));

            if (mimeType.Equals("font/ttf", StringComparison.OrdinalIgnoreCase) || mimeType.Equals("font/truetype", StringComparison.OrdinalIgnoreCase) || mimeType.Equals("application/x-font-ttf", StringComparison.OrdinalIgnoreCase))
            {
                src = src.Substring(src.IndexOf("base64,") + 7);
                src = src.TrimEnd(')').TrimEnd('\"').TrimEnd(')');
                byte[] fontBytes = Convert.FromBase64String(src);

                string tempFile = Path.GetTempFileName();

                File.WriteAllBytes(tempFile, fontBytes);

                FontFamily family = new FontFamily(tempFile);
                return new KeyValuePair<string, FontFamily>(fontFamilyName, family);
            }

            return null;
        }

        private const string CSSDelimiters = ":;,{}";

        private static string ReadCSSToken(StringReader reader)
        {
            StringBuilder tbr = new StringBuilder();

            bool openQuotes = false;
            int openParentheses = 0;

            int c = reader.Read();
            if (c >= 0)
            {
                tbr.Append((char)c);

                if ((char)c == '"')
                {
                    openQuotes = !openQuotes;
                }

                if ((char)c == '(')
                {
                    openParentheses++;
                }
                if ((char)c == ')')
                {
                    openParentheses--;
                }


                while (c >= 0 && (!CSSDelimiters.Contains((char)c) || openQuotes || openParentheses > 0))
                {
                    c = reader.Read();
                    tbr.Append((char)c);
                    if ((char)c == '"')
                    {
                        openQuotes = !openQuotes;
                    }
                    if ((char)c == '(')
                    {
                        openParentheses++;
                    }
                    if ((char)c == ')')
                    {
                        openParentheses--;
                    }
                    c = reader.Peek();
                }
            }

            string val = tbr.ToString().Trim();

            return (string.IsNullOrEmpty(val) && c >= 0) ? ReadCSSToken(reader) : val;
        }
    }
}
