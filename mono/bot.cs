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
	private Cars otherCars;
	
	private void UpdateCarPositions(List<Car> newPositions){
		foreach(Car newPosition in newPositions){
			if(newPosition.EqualsWithCar(myCar)) {
				myCar.updateCarPosition(newPosition, currentTrack);
			} else {
				foreach(Car car in otherCars.cars){
					if(newPosition.EqualsWithCar(car)){
						car.updateCarPosition(newPosition, currentTrack);
					}
				}
			}
		}
	}
	
	private void GetTrackCorners(Track track){
				
	}
	
	private Track CreateTrack(GameInit gameinit){
		List<Piece> trackpieces = new List<Piece>();
		int index = 0;
		foreach(Piece piece in gameinit.data.race.track.pieces){
			Piece pieceWithIndex = piece;
			piece.index = index;
			trackpieces.Add(pieceWithIndex);
		}
		Track track = new Track();
		track.id = gameinit.data.race.track.id;
		track.lanes = gameinit.data.race.track.lanes;
		track.name = gameinit.data.race.track.name;
		track.pieces = trackpieces;
		track.startingPoint = gameinit.data.race.track.startingPoint;
		return track;
	}
	
	private SendMsg DetermineAction(){
		if(myCar.piecePosition.pieceIndex > 1 && myCar.piecePosition.pieceIndex < 4 && myCar.piecePosition.lane.startLaneIndex == 0 && myCar.piecePosition.lane.endLaneIndex == 0){
			if(currentTrack.GetNextPiece(myCar).@switch){
				return new SwitchLane("Right");
			}
		}
		if(myCar.piecePosition.pieceIndex > 4 && myCar.piecePosition.pieceIndex < 10 && myCar.piecePosition.lane.startLaneIndex == 1 && myCar.piecePosition.lane.endLaneIndex == 1){
			if(currentTrack.GetNextPiece(myCar).@switch){
				return new SwitchLane("Left");
			}
		}
		if(myCar.piecePosition.pieceIndex > 15 && myCar.piecePosition.pieceIndex < 21 && myCar.piecePosition.lane.startLaneIndex == 0 && myCar.piecePosition.lane.endLaneIndex == 0){
			if(currentTrack.GetNextPiece(myCar).@switch){
				return new SwitchLane("Right");
			}
		}
		
		if(currentTrack.GetNextPiece(myCar).angle == 0){ //If next piece is straight, full throttle
			return new Throttle(1.0);
		}
		else {
			Piece nextPiece = currentTrack.GetNextPiece(myCar);
			if((nextPiece.angle > 30 || nextPiece.angle < -30) && nextPiece.radius < 150){ //if we have tight curve ahead, slow down
				if(myCar.speed < 6.41112){
					return new Throttle(1.0);
				}else{
					return new Throttle(0.1);
				}
			}
			return new Throttle(1.0); //slow only to tight curves
		}
	}

	Bot(StreamReader reader, StreamWriter writer, Join join) {
		this.writer = writer;
		string line;

		send(join);

		while((line = reader.ReadLine()) != null) {
			MsgWrapper msg = JsonConvert.DeserializeObject<MsgWrapper>(line);
			switch(msg.msgType) {
				case "yourCar":
					YourCar yourCar = JsonConvert.DeserializeObject<YourCar>(line);
					myCar = new Car();
					myCar.id = yourCar.data;
					send(new Ping());
				break;
				case "carPositions":
					CarPositions carPositions = JsonConvert.DeserializeObject<CarPositions>(line);
					UpdateCarPositions(carPositions.data);
					Console.WriteLine("Car startlaneindex:"+myCar.piecePosition.lane.startLaneIndex+" Car endlaneIndex:"+myCar.piecePosition.lane.endLaneIndex+" Current tick: "+carPositions.gameTick);
					send(DetermineAction());
					break;
				case "join":
					Console.WriteLine("Joined");
					send(new Ping());
					break;
				case "crash":
					CrashMsg crashMsg = JsonConvert.DeserializeObject<CrashMsg>(line);
					if(myCar.HasId(crashMsg.data)){ //Houston we have crashed...
						Console.WriteLine("CRASHED AT GAMETICK:"+crashMsg.gameTick);
						Console.WriteLine("speed: "+myCar.speed);
						Piece crashPiece = currentTrack.pieces[myCar.piecePosition.pieceIndex];
						Console.WriteLine("crashpiece index: "+myCar.piecePosition.pieceIndex);
						Console.WriteLine("crashpiece angle: "+crashPiece.angle);
						Console.WriteLine("crashpiece radius: "+crashPiece.radius);
						Console.WriteLine("crashpiece length: "+crashPiece.length);

				       	using (System.IO.StreamWriter file = new System.IO.StreamWriter("./crashes.txt", true))
        				{
           					file.WriteLine("CRASHED AT GAMETICK:"+crashMsg.gameTick);
							file.WriteLine("speed: "+myCar.speed);
							file.WriteLine("crashpiece index: "+myCar.piecePosition.pieceIndex);
							file.WriteLine("crashpiece angle: "+crashPiece.angle);
							file.WriteLine("crashpiece radius: "+crashPiece.radius);
							file.WriteLine("crashpiece length: "+crashPiece.length);
							file.WriteLine("END");							
        				}
					}
					send(new Ping());
					break;
				case "gameInit":
					Console.WriteLine("Race init");
					GameInit gameInit = JsonConvert.DeserializeObject<GameInit>(line);
					currentTrack = CreateTrack(gameInit);
					otherCars = new Cars(gameInit.data.race.cars);
					otherCars.cars.Remove(otherCars.GetCarById(myCar.id));
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

public class Corner {
	public List<Piece> pieces { get; set; }
	public double angle { get; set; }
	public double radius { get; set; }
}

public class Cars {
	
	public Cars(List<Car> cars){
		this.cars = cars;
	}
	
	public List<Car> cars { get; set; }
	public Car GetCarById(Id id){
		foreach(Car car in cars){
			if(car.id.name == id.name && car.id.color == id.color){
				return car;
			}
		}
		return null;
	}
}


/*******CRASH**************/
public class CrashMsg
{
    public string msgType { get; set; }
    public Id data { get; set; }
    public string gameId { get; set; }
    public int gameTick { get; set; }
}
 
/*******YOUR CAR***********/
public class YourCar {
	public string msgType { get; set; }
	public Id data { get; set; }
}

/*******GAME INIT**********/
public class Piece
{
	public Piece(){
		this.@switch = false;
		this.radius = 0;
		this.angle = 0;
	}
	
    public double length { get; set; }
    public bool @switch { get; set; }
    public int radius { get; set; }
    public double angle { get; set; }
	public int index { get; set; }
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
	
	public Piece GetNextPiece(Car car){
		if(car.piecePosition.pieceIndex + 1 < pieces.Count){
			return pieces[car.piecePosition.pieceIndex + 1];
		} else {
			return pieces[0];
		}
	}
	public Piece GetNextPieceX(Car car, int forward)
	{
		if(pieces.Count > car.piecePosition.pieceIndex+forward)
			return pieces[car.piecePosition.pieceIndex+forward];
		else
			return pieces[car.piecePosition.pieceIndex+forward - (pieces.Count)];
	}
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
	public Car(){
		this.speed = 0.0;
	}
	
    public Id id { get; set; }
    public double angle { get; set; }
    public PiecePosition piecePosition { get; set; }
	public Dimensions dimensions { get; set; }
	public double speed { get; set; }
	
	public bool EqualsWithCar(Car car){
		if(car.id.name == this.id.name && car.id.color == this.id.color){
			return true;
		}
		return false;
	}
	public bool HasId(Id id){
		if(id.name == this.id.name && id.color == this.id.color){
			return true;
		}
		return false;
	}
	
	private void calculateSpeed(Car newspeed, Track currentTrack){
		if(this.piecePosition != null){
			if(this.piecePosition.pieceIndex == newspeed.piecePosition.pieceIndex){
				this.speed = newspeed.piecePosition.inPieceDistance - this.piecePosition.inPieceDistance;
			}else{
				if(currentTrack.pieces[this.piecePosition.pieceIndex].length > 0){ //Previous piece length was known, otherwise just quess that speed hasn't changed.
					this.speed = currentTrack.pieces[this.piecePosition.pieceIndex].length - this.piecePosition.inPieceDistance + newspeed.piecePosition.inPieceDistance;
				}
			}
		}
	}
	
	public void updateCarPosition(Car car, Track currentTrack){
		calculateSpeed(car, currentTrack);
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