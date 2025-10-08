namespace Calculator
{
    using System;

    class Program
    {
        static void Main(string[] args)
        {
            findArea(26, 16, 1.75, 177);
            findArea(25, 15, 1.75, 177);
            findArea(24, 14, 1.75, 177);
        }

        static double area(double width, double height)
        {
            return width * height;
        }

        static void findArea(double length1, double length2, double width, double targetArea)
        {
            double totalArea = 0;
            while (totalArea < targetArea)
            {
                if (totalArea < targetArea)
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