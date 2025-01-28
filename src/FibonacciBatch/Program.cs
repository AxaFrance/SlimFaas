

foreach(string arg in args){
    int i = int.Parse(arg);
    var fibonacci = new Fibonacci();
    Console.WriteLine(fibonacci.Run(i));
}

internal class Fibonacci
{
    public int Run(int i)
    {
        if (i <= 2)
        {
            return 1;
        }

        return Run(i - 1) + Run(i - 2);
    }
}
