using RaftNode;


switch (args.LongLength)
{
    case 0:
    case 1:
        Console.WriteLine("Port number and protocol are not specified");
        break;
    case 2:
        await Starter.StartNode(args[0], int.Parse(args[1]));
        break;
    case 3:
        await Starter.StartNode(args[0], int.Parse(args[1]), "localhost", args[2]);
        break;
}
