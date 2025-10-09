namespace Calculator
{
    using System;
    using System.Formats.Asn1;
    using System.Runtime.CompilerServices;

    class Program
    {
        static void Main(string[] args)
        {
            FindArea();
        }

        static double area(double width, double height)
        {
            return width * height;
        }

        static double Length()
        {
            double length;
            Console.WriteLine("Enter length: ");
            length = Convert.ToDouble(Console.ReadLine());
            return length;
        }

        static double Width()
        {
            double width;
            Console.WriteLine("Enter width: ");
            width = Convert.ToDouble(Console.ReadLine());
            return width;
        }

        static double TargetArea()
        {
            double filterArea;

            filterArea = area(Length(), Width());
            Console.WriteLine("Filter Area: " + filterArea);
            return filterArea;
        }
        static void FindArea()
        {
            double totalArea = 0;
            double width = 0;


            while (totalArea < TargetArea())
            {
                if (totalArea < TargetArea())
                {
                    totalArea = (Length() * width * 2) + (Width() * width * 2);
                    Console.WriteLine("Total area: " + totalArea);
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
    
    public class Area
    {
        
    }
}