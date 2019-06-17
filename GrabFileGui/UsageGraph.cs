using System.Windows.Media;
using System.Windows;
using System.Windows.Shapes;

namespace GrabFileGui
{
    class UsageGraph
    {
        private double xMin;
        private double xMax;
        private double yMin;
        private double yMax;
        public double margin { get; set; }
        private GeometryGroup xGeo;
        private GeometryGroup yGeo;
        private PointCollection points;


        public UsageGraph(double xmin, double xmax, double ymin, double ymax, double xactual, double yactual, double graphmargin)
        {
            margin = graphmargin;
            xMin = xmin;
            xMax = xmax;
            yMin = ymin;
            yMax = ymax;
            points = new PointCollection();

            //instantiate x-axis
            xGeo = new GeometryGroup();
            xGeo.Children.Add(new LineGeometry(new Point(0, yMax), new Point(xactual, yMax)));

            //instantiate y-axis
            yGeo = new GeometryGroup();
            yGeo.Children.Add(new LineGeometry(new Point(xMin, 0), new Point(xMin, yactual)));
        }

        public Path getXaxis()
        {
            Path path = new Path();
            path.StrokeThickness = 1;
            path.Stroke = Brushes.Black;
            path.Data = xGeo;
            return path;
        }

        public Path getYaxis()
        {
            Path path = new Path();
            path.StrokeThickness = 1;
            path.Stroke = Brushes.Black;
            path.Data = yGeo;
            return path;
        }

        public Polyline getLine()
        {
            Polyline newLine = new Polyline();
            newLine.StrokeThickness = 1;
            newLine.Stroke = Brushes.Blue;
            newLine.Points = points;
            return newLine;
        }

        public void addNewPoint(double yValue)
        {
            yValue = yMax - yValue*100; //y-value is inverted, so subtract from the max to make it look better
            if(points.Count == 0)
            {
                points.Add(new Point(xMin, yValue));
                //points.Add(new Point(xMax, yValue));
            }
            else
            {
                if(points.Count >= 35)
                {
                    points.RemoveAt(0);
                }
                double averageDistance = (xMax-xMin) / (points.Count);
                double Xindex = xMin;
                for(int i = 0; i < points.Count; i++)
                {
                    points[i] = new Point(Xindex, points[i].Y);
                    Xindex = Xindex + averageDistance;
                }
                points.Add(new Point(Xindex, yValue));
            }

        }
    }
}
