namespace Calculator
{
    using System;

    public class Program
    {
        static void Main(string[] args)
        {
            double width, length, area;
            width = Area.FindWidth();
            length = Area.FindLength();
            area = Area.FindArea(width, length);
            Console.WriteLine("The area is: " + area);
            Console.WriteLine("Would you like to change the width and length of the outside panel?\n1. yes\n2. no");
            int responseInt = Convert.ToInt32(Console.ReadLine());
            switch (responseInt)
            {
                case 1:
                    width = Area.FindWidth();
                    length = Area.FindLength();
                    break;
                case 2:
                    break;
                default:
                    Console.WriteLine("Invalid input");
                    break;
            }
            Area.FindTargetArea(width, length, area);


        }
        
        
    }

    public class Area
    {
        public static double FindLength()
        {
            double length;
            Console.WriteLine("Enter length: ");
            length = Convert.ToDouble(Console.ReadLine());
            return length;
        }

        public static double FindWidth()
        {
            double width;
            Console.WriteLine("Enter width: ");
            width = Convert.ToDouble(Console.ReadLine());
            return width;
        }

        public static double FindArea(double length, double width)
        {
            return length * width;
        }

        public static void FindTargetArea(double width, double length, double area) 
        {
            double targetArea = 0;
            double height = 0;
            while (targetArea < area)
            {
                if (targetArea < area)
                {
                    targetArea = (width * height * 2) + (length * height * 2);
                    height += 0.125;
                    Console.WriteLine(targetArea + ", " + height);
                }
            }
            Console.WriteLine("The target area is: " + targetArea + " at height: " + height);
        }
    }
}