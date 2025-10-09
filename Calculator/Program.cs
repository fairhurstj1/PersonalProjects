namespace Calculator
{
    using System;

    class Program
    {
        static void Main(string[] args)
        {
            
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
            Console.WriteLine("Width: " + width + " Area: " + totalArea);
        }

 

    }
}