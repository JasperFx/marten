using System;
using System.Collections.Generic;
using System.ComponentModel;
using Baseline;

namespace Marten.Testing.Fixtures
{
    public enum Colors
    {
        Red,
        Blue,
        Green
    }

    public class Target
    {
        private static Random _random = new Random(67);

        private static string[] _strings = new[]
        {"Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Violet", "Pink", "Gray", "Black"};

        private static string[] _otherStrings = new[]
            {"one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"};

        public static IEnumerable<Target> GenerateRandomData(int number)
        {
            var i = 0;
            while (i < number)
            {
                yield return Random(true);

                i++;
            }
        } 

        public static Target Random(bool deep = false)
        {
            var target = new Target();
            target.String = _strings[_random.Next(0, 10)];
            target.AnotherString = _otherStrings[_random.Next(0, 10)];
            target.Number = _random.Next();

            target.NumberArray = new int[] {_random.Next(0, 10), _random.Next(0, 10), _random.Next(0, 10) };

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

            target.Long = 100*_random.Next();
            target.Double = _random.NextDouble();
            target.Long = _random.Next()*10000;

            target.Date = DateTime.Today.AddDays(_random.Next(1, 100));

            if (deep)
            {
                target.Inner = Random();

                var number = _random.Next(1, 10);
                target.Children = new Target[number];
                for (int i = 0; i < number; i++)
                {
                    target.Children[i] = Random();
                }
            }

            return target;
        }

        public Target()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public int Number { get; set; }
        public long Long { get; set; }
        public string String { get; set; }
        public string AnotherString { get; set; }

        public Guid OtherGuid { get; set; }

        public Target Inner { get; set; }

        public Colors Color { get; set; }

        public bool Flag { get; set; }

        public double Double { get; set; }
        public decimal Decimal { get; set; }
        public DateTime Date { get; set; }
        public DateTimeOffset DateOffset { get; set; }

        public int[] NumberArray { get; set; }

        

        public Target[] Children { get; set; }

        public int? NullableNumber { get; set; }

    }

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