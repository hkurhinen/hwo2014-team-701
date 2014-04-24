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
	private double limitSpeed = 9;
	private double deceleration = 0;
	private Corners trackCorners;
	private double throttle = 0;
	private double startSpeed;
	private double startDist;
	private List<LearningData> learnedData;
	
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
	
	private SendMsg GetDeceleration(){
		if(myCar.speed == 0){
			throttle = 1;
			return new Throttle(1.0);
		} else {
			if(throttle == 0){
				double speedLowered = startSpeed - myCar.speed;
				double distanceTraveled = myCar.piecePosition.inPieceDistance - startDist;
				Console.WriteLine("Start speed: "+startSpeed);
				Console.WriteLine("Current speed: "+myCar.speed);
				Console.WriteLine("Start distance: "+startDist);
				Console.WriteLine("Distance traveled: "+distanceTraveled);
				Console.WriteLine("Speed lowered: "+speedLowered);
				Console.WriteLine("End distance: "+myCar.piecePosition.inPieceDistance);
				Console.WriteLine("End speed: "+myCar.speed);
				deceleration = speedLowered / distanceTraveled;
				return new Throttle(1.0);
			}else{
				if(myCar.speed < 1){
					return new Throttle(1);
				}else{					
					startSpeed = myCar.speed;
					startDist = myCar.piecePosition.inPieceDistance;
					throttle = 0;
					return new Throttle(0);
				}
			}
		}
		
	}
	
	private LearningData GetLearnedDataById(int id){
		foreach(LearningData d in learnedData){
			if(d.cornerIndex == id){
				return d;
			}
		}
		return null;
	}
	
	private Corners GetTrackCorners(Track track){
		List<Corner> corners = new List<Corner>();
		int cornerIndex = 0;
		for(int i =  0; i < track.pieces.Count;i++){
			if(track.pieces[i].angle != 0){
				Piece previousPiece = track.pieces[i];
				Corner corner = new Corner();
				corner.cornerIndex = cornerIndex;
				cornerIndex++;
				double cornerangle = 0.0;
				while((track.pieces[i].angle < 0 && previousPiece.angle < 0) || (track.pieces[i].angle > 0 && previousPiece.angle > 0)) {
					corner.pieces.Add(track.pieces[i]);
					cornerangle += track.pieces[i].angle;
					previousPiece = track.pieces[i];
					i++;
				}
				corner.angle = cornerangle;
				double maxRad = corner.pieces[0].radius;
				double minRad = corner.pieces[0].radius;
				foreach(Piece cornerPiece in corner.pieces){
					if(cornerPiece.radius > maxRad) {
						maxRad = cornerPiece.radius;
					}
					if(cornerPiece.radius < minRad) {
						minRad = cornerPiece.radius;
					}
				}
				if(GetLearnedDataById(corner.cornerIndex) != null){
					corner.maxSpeed = GetLearnedDataById(corner.cornerIndex).maxspeed;
				}else{
					corner.maxSpeed = 10;
				}
				corner.maxRadius = maxRad;
				corner.minRadius = minRad;
				corners.Add(corner);
			}
		}
		
		return new Corners(corners);
	}
	
	private void LoadLearnedData(Track track) {
		if(File.Exists("./"+track.name+".json")){
			learnedData = JsonConvert.DeserializeObject<List<LearningData>>(File.ReadAllText("./"+track.name+".json"));
		}else{
			learnedData = new List<LearningData>();
		}
	}
	private void UpdateLearnedData(LearningData newData){
		for(int i = 0; i < learnedData.Count; i++){
			if(learnedData[i].cornerIndex == newData.cornerIndex){
				learnedData[i].maxspeed = newData.maxspeed;
				string json = JsonConvert.SerializeObject(learnedData);
				System.IO.File.WriteAllText("./"+currentTrack.name+".json", json);
			}
		}
	}
	
	private Track CreateTrack(GameInit gameinit){
		List<Piece> trackpieces = new List<Piece>();
		int index = 0;
		foreach(Piece piece in gameinit.data.race.track.pieces){
			Piece pieceWithIndex = piece;
			piece.index = index;
			trackpieces.Add(pieceWithIndex);
			index++;
		}
		Track track = new Track();
		track.id = gameinit.data.race.track.id;
		track.lanes = gameinit.data.race.track.lanes;
		track.name = gameinit.data.race.track.name;
		track.pieces = trackpieces;
		track.startingPoint = gameinit.data.race.track.startingPoint;
		return track;
	}
	
	private Double GetDistanceUntilPiece(Piece p){
		double dist = 0;
		if(myCar.piecePosition.pieceIndex < p.index){
			for(int i = myCar.piecePosition.pieceIndex;i < p.index;i++){
				dist += currentTrack.pieces[i].length;
			}
		
		}else{
			for(int i = myCar.piecePosition.pieceIndex; i < currentTrack.pieces.Count;i++){
				dist += currentTrack.pieces[i].length;
			}
			for(int j = 0; j < p.index;j++){
				dist += currentTrack.pieces[j].length;
			}
		}
		return dist - myCar.piecePosition.inPieceDistance;
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
		
		/*if(currentTrack.GetNextPiece(myCar).angle == 0){ //If next piece is straight, full throttle
			return new Throttle(1.0);
		}
		else {
			Piece nextPiece = currentTrack.GetNextPiece(myCar);
			Piece secondNextPiece = currentTrack.GetNextPieceX(myCar,2);
			Piece thirdNextPiece = currentTrack.GetNextPieceX(myCar,3);
			if((nextPiece.angle > 30 || nextPiece.angle < -30 || secondNextPiece.angle > 30 || secondNextPiece.angle < -30 || thirdNextPiece.angle > 30 || thirdNextPiece.angle < -30)){ //if we have tight curve ahead, slow down
				Corner nextCorner = trackCorners.GetCornerByPiece(nextPiece);
				if(myCar.speed < 1.0/*(limitSpeed / (nextCorner.angle / nextCorner.maxRadius))) {
			/*		return new Throttle(1.0);
				} else {
					return new Throttle(0.1);
				}
			}
			return new Throttle(1.0); //slow only to tight curves
		}*/
		
		if(currentTrack.pieces[myCar.piecePosition.pieceIndex].angle != 0){
			Corner currentCorner = trackCorners.GetCornerByPiece(currentTrack.pieces[myCar.piecePosition.pieceIndex]);
			if(currentCorner == null){
				//Console.WriteLine("null");
				return new Throttle(1.0);
			}
			
			double pieceLimitSpeed; //= limitSpeed / (currentCorner.angle / currentCorner.minRadius);
			if(GetLearnedDataById(currentCorner.cornerIndex) != null){
				pieceLimitSpeed = GetLearnedDataById(currentCorner.cornerIndex).maxspeed;
			}
			else{
				pieceLimitSpeed = 10;
			}
			if(pieceLimitSpeed < 0){
				pieceLimitSpeed = -pieceLimitSpeed;
			}
			Piece nextPiece = currentTrack.GetNextPiece(myCar);
			//Console.WriteLine("this piece limit"+pieceLimitSpeed);
			if(myCar.speed < pieceLimitSpeed || (nextPiece.angle < 30 && nextPiece.angle > -30)){
				return new Throttle(1.0);
			}else{
				return new Throttle(0.0);
			}
		
		}else{		
			Corner nextCorner = trackCorners.GetNextCorner(myCar);
			//double nextCornerLength = nextCorner.angle * Math.PI * nextCorner.minRadius;
			double nextCornerEntrySpeed;//limitSpeed / (nextCorner.angle / nextCorner.minRadius);
			if(GetLearnedDataById(nextCorner.cornerIndex) != null){
				nextCornerEntrySpeed = GetLearnedDataById(nextCorner.cornerIndex).maxspeed;
			}else{
				nextCornerEntrySpeed = 10;
			}
			if(nextCornerEntrySpeed < 0){
				nextCornerEntrySpeed = -nextCornerEntrySpeed;
			}
			
			double speedAfterBreaking = myCar.speed - (GetDistanceUntilPiece(nextCorner.pieces[0]) * deceleration);
			//Console.WriteLine("next corner limit"+nextCornerEntrySpeed);
			if(speedAfterBreaking > nextCornerEntrySpeed){
				return new Throttle(0.0);
			}else{
				return new Throttle(1.0);
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
				case "yourCar":
					YourCar yourCar = JsonConvert.DeserializeObject<YourCar>(line);
					myCar = new Car();
					myCar.id = yourCar.data;
					send(new Ping());
				break;
				case "carPositions":
					CarPositions carPositions = JsonConvert.DeserializeObject<CarPositions>(line);
					UpdateCarPositions(carPositions.data);
					//Console.WriteLine("Car startlaneindex:"+myCar.piecePosition.lane.startLaneIndex+" Car endlaneIndex:"+myCar.piecePosition.lane.endLaneIndex+" Current tick: "+carPositions.gameTick);
					if(deceleration == 0){
						send(GetDeceleration());
					}else{
						send(DetermineAction());
					}
					break;
				case "join":
					Console.WriteLine("Joined");
					send(new Ping());
					break;
				case "crash":
					CrashMsg crashMsg = JsonConvert.DeserializeObject<CrashMsg>(line);
					if(myCar.HasId(crashMsg.data)){ //Houston we have crashed...
						Console.WriteLine("#######CRASH!!##############");
						Corner crashCorner = trackCorners.GetCornerByPiece(currentTrack.pieces[myCar.piecePosition.pieceIndex]);
						if(crashCorner != null){
							if(GetLearnedDataById(crashCorner.cornerIndex) != null){
								LearningData lData = GetLearnedDataById(crashCorner.cornerIndex);
								lData.maxspeed = lData.maxspeed - 0.01;
								//UpdateLearnedData(lData);
							}else{
								LearningData lData = new LearningData();
								lData.cornerIndex = crashCorner.cornerIndex;
								lData.maxspeed = myCar.speed - 0.1;
								learnedData.Add(lData);
							    String serializedData = JsonConvert.SerializeObject(learnedData);
								//File.WriteAllText("./"+currentTrack.name+".json", serializedData);
							}
						}
						Double newspeed = myCar.speed - 0.1;
						Console.WriteLine("Setting cornerspeed to "+newspeed);
						Console.WriteLine("############################");
					}
					send(new Ping());
					break;
				case "gameInit":
					Console.WriteLine("Race init");
					GameInit gameInit = JsonConvert.DeserializeObject<GameInit>(line);
					currentTrack = CreateTrack(gameInit);
					otherCars = new Cars(gameInit.data.race.cars);
					otherCars.cars.Remove(otherCars.GetCarById(myCar.id));
					LoadLearnedData(currentTrack);
					trackCorners = GetTrackCorners(currentTrack);
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
		//Console.WriteLine(msg.ToJson());
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

public class LearningData {
	public int cornerIndex { get; set; }
	public double maxspeed { get; set; }
}

public class Corners {
	
	public Corners(List<Corner> corners){
		this.corners = corners;
	}
	
	List<Corner> corners { get; set; }

	public Corner GetNextCorner(Car car){ //returns next corner or currentcorner if already in it.
		int currentPieceIndex = car.piecePosition.pieceIndex;
		foreach(Corner corner in corners){
			if(corner.pieces[corner.pieces.Count - 1].index > currentPieceIndex){ 
				return corner;
			}
		}
		return corners[0];
	}
	public Corner GetCornerByPiece(Piece piece){
		foreach(Corner corner in corners){
			if(corner.ContainsPiece(piece)){
				return corner;
			}
		}
		return null;
	}
}

public class Corner {
	public Corner(){
		pieces = new List<Piece>();
	}
	public List<Piece> pieces { get; set; }
	public double angle { get; set; }
	public double minRadius { get; set; }
	public double maxRadius { get; set; }
	public double maxSpeed { get; set; }
	public int cornerIndex { get; set; } 
	
	public bool ContainsPiece(Piece piece){
		foreach(Piece p in pieces){
			if(p.index == piece.index){
				return true;
			}
		}
		return false;
	}
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
	public Piece GetPieceByIndex(int index){
		foreach(Piece piece in pieces){
			if(piece.index == index){
				return piece;
			}
		}
		return null;
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
				double previouslength;
				if(currentTrack.pieces[this.piecePosition.pieceIndex].length > 0){
					previouslength = currentTrack.pieces[this.piecePosition.pieceIndex].length;
				}else{
					double totalradius;
					if(currentTrack.pieces[this.piecePosition.pieceIndex].angle > 0){
						totalradius = currentTrack.pieces[this.piecePosition.pieceIndex].radius - currentTrack.lanes[this.piecePosition.lane.startLaneIndex].distanceFromCenter;
					}else{
						totalradius = currentTrack.pieces[this.piecePosition.pieceIndex].radius + currentTrack.lanes[this.piecePosition.lane.startLaneIndex].distanceFromCenter;
					}
					previouslength = (currentTrack.pieces[this.piecePosition.pieceIndex].angle / 360) * 2 * Math.PI * totalradius;
				}
				
				this.speed = previouslength - this.piecePosition.inPieceDistance + newspeed.piecePosition.inPieceDistance;
				
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

public class BotId
{
	public BotId(string name, string key){
		this.name = name;
		this.key = key;
	}
    public string name { get; set; }
    public string key { get; set; }
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
    public string name { get; set; }
    public string key { get; set; }

	public Join(string name, string key) {
		this.name = name;
		this.key = key;
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

class JoinRace: SendMsg {
	public BotId botId { get; set; }
    public string trackName { get; set; }

	public JoinRace(string name, string key, string trackName) {
		this.botId = new BotId(name, key);
		this.trackName = trackName;
	}

	protected override string MsgType() { 
		return "joinRace";
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