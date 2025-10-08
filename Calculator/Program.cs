namespace Calculator
{
    using System;

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("hello world");
            const double length1 = 24;
            const double length2 = 14;
            const double width = 1.75;
            double area1 = (area(length1, width) * 2) + (area(length2, width) * 2);
            Console.WriteLine(area1);
        }

        static double area(double width, double height)
        {
            return width * height;
        }

 

    }
}