using System;

namespace EasyAccess.Samples.Console.Types
{
    public readonly struct HTMLContent : IEquatable<HTMLContent>
    {
        private readonly string _content;

        private HTMLContent(string value) =>
            _content = value;

        public static HTMLContent? Maybe(string input) =>
            TryParse(input, out HTMLContent htmlContent)
            ? htmlContent
            : default(HTMLContent?);

        public static bool TryParse(string input, out HTMLContent htmlContent)
        {
            if (!string.IsNullOrWhiteSpace(input)
                && input.Length >= 4
                && input.Contains("<")
                && input.Contains(">"))
            {
                htmlContent = new HTMLContent(input);
                return true;
            }

            htmlContent = default;
            return false;
        }

        public static HTMLContent Parse(string input) =>
            Maybe(input) ?? throw new FormatException();

        public override string ToString() =>
            _content ?? "</>";

        public override bool Equals(object obj) =>
            obj is HTMLContent htmlContent && Equals(htmlContent);

        public bool Equals(HTMLContent other) =>
            ToString().Equals(other._content.ToString());

        public override int GetHashCode() =>
            _content.Length;

        public static bool operator ==(HTMLContent left, HTMLContent right) =>
            left.Equals(right);

        public static bool operator !=(HTMLContent left, HTMLContent right) =>
            !left.Equals(right);

        public static explicit operator string(HTMLContent value) =>
            value._content;
    }
}
