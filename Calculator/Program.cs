namespace Calculator
{
    using System;

    class Program
    {
        static void Main(string[] args)
        {
            findArea();
        }

        static double area(double width, double height)
        {
            return width * height;
        }

        static void findArea()
        {
            double totalArea = 0;
            const double length1 = 25;
            const double length2 = 15;
            double width = 1.75;
            while (totalArea < 177)
            {
                if (totalArea < 177)
                {
                    totalArea = (area(length1, width) * 2) + (area(length2, width) * 2);
                    Console.WriteLine(totalArea);
                    width += 0.25;
                }
                else
                {
                    break;
                }
            }
            Console.WriteLine("Width: " + width);
        }

 

    }
}