using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Baseline;

namespace Marten.Schema.Testing.Documents
{
    public class TargetIntId
    {
        private static readonly Random _random = new Random(67);

        private static readonly string[] _strings =
        {
            "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Violet",
            "Pink", "Gray", "Black"
        };

        private static readonly string[] _otherStrings =
        {
            "one", "two", "three", "four", "five", "six", "seven", "eight",
            "nine", "ten"
        };

        public static IEnumerable<TargetIntId> GenerateRandomData(int number)
        {
            var i = 0;
            while (i < number)
            {
                yield return Random(true);

                i++;
            }
        }

        public static TargetIntId Random(bool deep = false)
        {
            var target = new TargetIntId();
            target.String = _strings[_random.Next(0, 10)];
            target.AnotherString = _otherStrings[_random.Next(0, 10)];
            target.Number = _random.Next();
            target.AnotherNumber = _random.Next();

            target.Flag = _random.Next(0, 10) > 5;

            target.Float = float.Parse(_random.NextDouble().ToString());

            target.NumberArray = new[] { _random.Next(0, 10), _random.Next(0, 10), _random.Next(0, 10) };

            target.NumberArray = target.NumberArray.Distinct().ToArray();

            switch (_random.Next(0, 2))
            {
                case 0:
                    target.Color = Colors.Blue;
                    break;

                case 1:
                    target.Color = Colors.Green;
                    break;

                default:
                    target.Color = Colors.Red;
                    break;
            }

            target.Long = 100 * _random.Next();
            target.Double = _random.NextDouble();
            target.Long = _random.Next() * 10000;

            target.Date = DateTime.Today.AddDays(_random.Next(-10000, 10000));

            if (deep)
            {
                target.Inner = Random();

                var number = _random.Next(1, 10);
                target.Children = new TargetIntId[number];
                for (int i = 0; i < number; i++)
                {
                    target.Children[i] = Random();
                }

                target.StringDict = Enumerable.Range(0, _random.Next(1, 10)).ToDictionary(i => $"key{i}", i => $"value{i}");
            }

            return target;
        }

        public TargetIntId()
        {
            StringDict = new Dictionary<string, string>();
        }

        public int Id { get; set; }

        public int Number { get; set; }

        public int AnotherNumber { get; set; }

        public long Long { get; set; }
        public string String { get; set; }
        public string AnotherString { get; set; }

        public Guid OtherGuid { get; set; }

        public TargetIntId Inner { get; set; }

        public Colors Color { get; set; }

        public Colors? NullableEnum { get; set; }

        public bool Flag { get; set; }

        [JsonInclude] // this is needed to make System.Text.Json happy
        public string StringField;

        public double Double { get; set; }
        public decimal Decimal { get; set; }
        public DateTime Date { get; set; }
        public DateTimeOffset DateOffset { get; set; }

        [JsonInclude] // this is needed to make System.Text.Json happy
        public float Float;

        public int[] NumberArray { get; set; }

        public string[] TagsArray { get; set; }

        public HashSet<string> TagsHashSet { get; set; }

        public TargetIntId[] Children { get; set; }

        public int? NullableNumber { get; set; }
        public DateTime? NullableDateTime { get; set; }
        public bool? NullableBoolean { get; set; }
        public Colors? NullableColor { get; set; }

        public IDictionary<string, string> StringDict { get; set; }

        public Guid UserId { get; set; }

        public class Address
        {
            public Address()
            {
            }

            public Address(string text)
            {
                var parts = text.ToDelimitedArray();
                Address1 = parts[0];
                City = parts[1];
                StateOrProvince = parts[2];
            }

            public string Address1 { get; set; }
            public string Address2 { get; set; }
            public string City { get; set; }
            public string StateOrProvince { get; set; }
            public string Country { get; set; }
            public string PostalCode { get; set; }

            public bool Primary { get; set; }
        }
    }
}
