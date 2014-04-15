using System;
using System.IO;
using System.Net.Sockets;
using System.Collections.Generic;
using Newtonsoft.Json;

public class Bot {
	public static void Main(string[] args) {
	    string host = args[0];
        int port = int.Parse(args[1]);
        string botName = args[2];
        string botKey = args[3];

		Console.WriteLine("Connecting to " + host + ":" + port + " as " + botName + "/" + botKey);

		using(TcpClient client = new TcpClient(host, port)) {
			NetworkStream stream = client.GetStream();
			StreamReader reader = new StreamReader(stream);
			StreamWriter writer = new StreamWriter(stream);
			writer.AutoFlush = true;

			new Bot(reader, writer, new Join(botName, botKey));
		}
	}

	private StreamWriter writer;
	private Track currentTrack;
	private Car myCar;
	private List<Car> cars;
	
	private void UpdateCarPositions(List<Car> newPositions){
		foreach(Car newPosition in newPositions){
			foreach(Car car in cars){
				if(newPosition.EqualsWithCar(car)){
					car.updateCarPosition(newPosition);
				}
			}
		}
	}

	Bot(StreamReader reader, StreamWriter writer, Join join) {
		this.writer = writer;
		string line;

		send(join);

		while((line = reader.ReadLine()) != null) {
			MsgWrapper msg = JsonConvert.DeserializeObject<MsgWrapper>(line);
			switch(msg.msgType) {
				case "carPositions":
					CarPositions carPositions = JsonConvert.DeserializeObject<CarPositions>(line);
					UpdateCarPositions(carPositions.data);
					send(new Throttle(0.6));
					break;
				case "join":
					Console.WriteLine("Joined");
					send(new Ping());
					break;
				case "gameInit":
					Console.WriteLine("Race init");
					GameInit gameInit = JsonConvert.DeserializeObject<GameInit>(line);
					currentTrack = gameInit.data.race.track;
					cars = gameInit.data.race.cars;
					send(new Ping());
					break;
				case "gameEnd":
					Console.WriteLine("Race ended");
					send(new Ping());
					break;
				case "gameStart":
					Console.WriteLine("Race starts");
					send(new Ping());
					break;
				default:
					send(new Ping());
					break;
			}
		}
	}

	private void send(SendMsg msg) {
		writer.WriteLine(msg.ToJson());
	}
}

class MsgWrapper {
    public string msgType;
    public Object data;

    public MsgWrapper(string msgType, Object data) {
    	this.msgType = msgType;
    	this.data = data;
    }
}

/*******GAME INIT**********/
public class Piece
{
    public double length { get; set; }
    public bool? @switch { get; set; }
    public int? radius { get; set; }
    public double? angle { get; set; }
}

public class Lane
{
    public int distanceFromCenter { get; set; }
    public int index { get; set; }
}

public class Position
{
    public double x { get; set; }
    public double y { get; set; }
}

public class StartingPoint
{
    public Position position { get; set; }
    public double angle { get; set; }
}

public class Track
{
    public string id { get; set; }
    public string name { get; set; }
    public List<Piece> pieces { get; set; }
    public List<Lane> lanes { get; set; }
    public StartingPoint startingPoint { get; set; }
}

public class Id
{
    public string name { get; set; }
    public string color { get; set; }
}

public class Dimensions
{
    public double length { get; set; }
    public double width { get; set; }
    public double guideFlagPosition { get; set; }
}

public class RaceSession
{
    public int laps { get; set; }
    public int maxLapTimeMs { get; set; }
    public bool quickRace { get; set; }
}

public class Race
{
    public Track track { get; set; }
    public List<Car> cars { get; set; }
    public RaceSession raceSession { get; set; }
}

public class Data
{
    public Race race { get; set; }
}

public class GameInit
{
    public string msgType { get; set; }
    public Data data { get; set; }
}

/******CAR POSITIONS*****/
public class CarCurrentLane
{
    public int startLaneIndex { get; set; }
    public int endLaneIndex { get; set; }
}

public class PiecePosition
{
    public int pieceIndex { get; set; }
    public double inPieceDistance { get; set; }
    public CarCurrentLane lane { get; set; }
    public int lap { get; set; }
}

public class Car
{
    public Id id { get; set; }
    public double angle { get; set; }
    public PiecePosition piecePosition { get; set; }
	public Dimensions dimensions { get; set; }
	
	public bool EqualsWithCar(Car car){
		if(car.id.name == this.id.name && car.id.color == this.id.color){
			return true;
		}
		return false;
	}
	
	public void updateCarPosition(Car car){
		this.angle = car.angle;
		this.piecePosition = car.piecePosition;
	}
}

public class CarPositions
{
    public string msgType { get; set; }
    public List<Car> data { get; set; }
    public string gameId { get; set; }
    public int gameTick { get; set; }
}

/******* MESSAGES TO SERVER **********/

abstract class SendMsg {
	public string ToJson() {
		return JsonConvert.SerializeObject(new MsgWrapper(this.MsgType(), this.MsgData()));
	}
	protected virtual Object MsgData() {
        return this;
    }

    protected abstract string MsgType();
}

class Join: SendMsg {
	public string name;
	public string key;
	public string color;

	public Join(string name, string key) {
		this.name = name;
		this.key = key;
		this.color = "red";
	}

	protected override string MsgType() { 
		return "join";
	}
}

class Ping: SendMsg {
	protected override string MsgType() {
		return "ping";
	}
}

class Throttle: SendMsg {
	public double value;

	public Throttle(double value) {
		this.value = value;
	}

	protected override Object MsgData() {
		return this.value;
	}

	protected override string MsgType() {
		return "throttle";
	}
}

class SwitchLane: SendMsg {
	public string lane;

	public SwitchLane(string lane){
		this.lane = lane;
	}

	protected override Object MsgData() {
		return this.lane;
	}

	protected override string MsgType(){
		return "switchLane";
	}
}
















