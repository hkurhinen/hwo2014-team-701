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
			//new Bot(reader, writer, new JoinRace(botName, botKey, "imola"));
		}
	}

	private StreamWriter writer;
	private Track currentTrack;
	private Car myCar;
	private Cars otherCars;
	//private Corners trackCorners;
	private double throttle = 0;
	private double prevThrottle = 0;
    private int currentGameTick;
	private double acceleration;
	private double friction;
	private double previousAngle = 0;
	private bool switchSent = false;
	private bool turboAvailable = false;
	private TurboDetails turboDetails;
	private double lapMaxAngle = 0;
	private double maxSpeedMultiplier;
	private double constant = 0.13;
	/**LINEAR REGRESSION VALUES**/
	private double[] xVals = new double[4];
	private double[] yVals = new double[4];
	private double rsquared;
	private double yintercept; //The y-intercept value of the line (i.e. y = ax + b, yintercept is b)
	private double slope; //The slop of the line (i.e. y = ax + b, slope is a).</param>
	/****************************/
	
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
				corner.maxRadius = maxRad;
				corner.minRadius = minRad;
				corners.Add(corner);
			}
		}
		
		return new Corners(corners);
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
	
	private Double GetLengthOfTurn(Car car, Piece piece){
		double totalradius = 0;
		if(piece.angle > 0){
			totalradius = piece.radius - currentTrack.lanes[car.piecePosition.lane.startLaneIndex].distanceFromCenter;
		}else{
			totalradius = piece.radius + currentTrack.lanes[car.piecePosition.lane.startLaneIndex].distanceFromCenter;
		}
		double sectorLength = piece.angle / 360 * 2 * Math.PI * totalradius;
		if(sectorLength < 0){
			sectorLength = -sectorLength;
		}
		return sectorLength;
		
	}
	
	private Double GetDistanceUntilPiece(Piece p){
		double dist = 0;
		if(myCar.piecePosition.pieceIndex < p.index){
			for(int i = myCar.piecePosition.pieceIndex;i < p.index;i++){
				if(currentTrack.pieces[i].angle != 0){
					dist += GetLengthOfTurn(myCar,currentTrack.pieces[i]);
				}else{
					dist += currentTrack.pieces[i].length;
				}
			}
		
		}else{
			for(int i = myCar.piecePosition.pieceIndex; i < currentTrack.pieces.Count;i++){
				if(currentTrack.pieces[i].angle != 0){
					dist += GetLengthOfTurn(myCar,currentTrack.pieces[i]);
				}else{
					dist += currentTrack.pieces[i].length;
				}
			}
			for(int j = 0; j < p.index;j++){
				if(currentTrack.pieces[j].angle != 0){
					dist += GetLengthOfTurn(myCar,currentTrack.pieces[j]);
				}else{
					dist += currentTrack.pieces[j].length;
				}
			}
		}
		return dist - myCar.piecePosition.inPieceDistance;
	}	

	private string GetNextSwitch (Car car)
	{
		foreach(Car otherCar in otherCars.cars){
			if(otherCar.piecePosition.pieceIndex == car.piecePosition.pieceIndex && otherCar.speed < myCar.speed && otherCar.piecePosition.lane.startLaneIndex == car.piecePosition.lane.startLaneIndex){
				if(otherCar.piecePosition.lane.startLaneIndex != otherCar.piecePosition.lane.endLaneIndex){ //if the other car is chaging, don't change.
					return "noswitch";
				}
				if(car.piecePosition.lane.startLaneIndex > 0 ){
					return "Left";
				}else{
					return "Right";
				}
			}
		}
		//int i = car.piecePosition.pieceIndex;
		int j = 2;
		//Piece upcomingSwitch = currentTrack.GetNextPiece(car);
		double totalangle = 0;
		if (currentTrack.GetNextPiece (car).@switch) {
			while (!currentTrack.GetNextPieceX(car, j).@switch) {
				//i++;
				j++;
				totalangle += currentTrack.GetNextPieceX(car, j).angle;
				Console.WriteLine("###############TOTAL ANGLE:"+totalangle+"##############################");
				//if (i == currentTrack.pieces.Count - 1) {
				//	i = 0;
				//}
			}
		}
		if (totalangle < 0) {
			//Console.WriteLine ("WANNA SWITCH LEFT!!");
			return "Left";
		} else {
			//Console.WriteLine ("WANNA SWITCH RIGHT!!");
			return "Right";
		}
	}


	private int GetNextTurn (Car car)
	{
		int i = car.piecePosition.pieceIndex;
		while(currentTrack.pieces[i].angle == 0){
			i++;
			if(i > currentTrack.pieces.Count - 1){
				i = 0;
			}
		}
		return i;
	}

	private Double GuessSlipAngle ()
	{
		return myCar.angularForce * 60 * 2;
	}

	private Double GetMaxSpeed ()
	{
		double totalradius = 0;
		if(currentTrack.pieces[myCar.piecePosition.pieceIndex].angle > 0){
			totalradius = currentTrack.pieces[myCar.piecePosition.pieceIndex].radius - currentTrack.lanes[myCar.piecePosition.lane.startLaneIndex].distanceFromCenter;
		}else{
			totalradius = currentTrack.pieces[myCar.piecePosition.pieceIndex].radius + currentTrack.lanes[myCar.piecePosition.lane.startLaneIndex].distanceFromCenter;
		}
		double maxspeed = Math.Sqrt(maxSpeedMultiplier * totalradius);
		if(ConnectedTurn() != null){
			double nextEntrySpeed = GetMaxEntrySpeed(ConnectedTurn());
			if(NeedToBreak(nextEntrySpeed, ConnectedTurn())){
				return 0.0;
			}
		}
		return maxspeed;
	}
	
	private Piece ConnectedTurn(){
		double currentAngle = currentTrack.pieces[myCar.piecePosition.pieceIndex].angle;
		int currentIndex = currentTrack.pieces[myCar.piecePosition.pieceIndex].index;
		while(currentTrack.pieces[currentIndex].angle != 0){
			if(currentTrack.pieces[currentIndex].angle != currentAngle){
				return currentTrack.pieces[currentIndex];
			}
			currentIndex++;
		}
		return null;
	}
	
	private Double GetMaxEntrySpeed (Piece p)
	{
		double totalradius = 0;
		if(p.angle > 0){
			totalradius = p.radius - currentTrack.lanes[myCar.piecePosition.lane.startLaneIndex].distanceFromCenter;
		}else{
			totalradius = p.radius + currentTrack.lanes[myCar.piecePosition.lane.startLaneIndex].distanceFromCenter;
		}
		return Math.Sqrt (maxSpeedMultiplier * totalradius);
	}
	
	private Double SpeedLostPerTick(){
		return friction * Math.Pow(myCar.speed, 2);
	}
	
	
	private double SpeedAfterNTicks(int ticks, double throttle, double startingSpeed){
		double speedAfterTicks = startingSpeed;
		for(int i = 0; i < ticks;i++){
			speedAfterTicks = NextTickSpeed(throttle, speedAfterTicks);
		}
		return speedAfterTicks;
	}
	
	private bool NeedToBreak(double reqSpeed, Piece p){
		if(myCar.speed == 0){
			return false;
		}
		double distanceUntilTurn = GetDistanceUntilPiece(p);

		int ticksUntilTurn = Convert.ToInt32(distanceUntilTurn / myCar.speed);
		if(SpeedAfterNTicks(ticksUntilTurn, 0.0, myCar.speed) > reqSpeed){
			return true;
		}
		return false;
	}
	
	private bool CanUseTurbo(){
		if(myCar.speed == 0){
			return false;
		}
		double distanceUntilTurn = GetDistanceUntilPiece(currentTrack.pieces[GetNextTurn(myCar)]);
		int ticksUntilTurn = Convert.ToInt32(distanceUntilTurn / myCar.speed);
		if(ticksUntilTurn < turboDetails.turboDurationTicks){
			return false;
		}
		double speedAfterTicks = myCar.speed;
		for(int i = 0; i < turboDetails.turboDurationTicks;i++){
			speedAfterTicks = nextTickSpeedTurbo(turboDetails.turboFactor, speedAfterTicks);
		}
		double speedAfterBreaking = SpeedAfterNTicks(ticksUntilTurn - turboDetails.turboDurationTicks,0.0,speedAfterTicks);
		if(speedAfterBreaking < GetMaxEntrySpeed(currentTrack.pieces[GetNextTurn(myCar)])){
			return true;
		}else{
			return false;
		}
		
	}
	
	
	private double nextTickSpeedTurbo(double turboFactor, double previousSpeed){
		double speendIncreased = previousSpeed * slope + yintercept;
		return previousSpeed + (turboFactor * speendIncreased);
	}
	
	private double NextTickSpeed(double throttle, double previousSpeed){
		double speendIncreased = previousSpeed * slope + (throttle * yintercept);
		return previousSpeed + speendIncreased;
	}
	
	private void LinearRegression(){
		double sumOfX = 0;
		double sumOfY = 0;
		double sumOfXSq = 0;
		double sumOfYSq = 0;
		double ssX = 0;
		double ssY = 0;
		double sumCodeviates = 0;
		double sCo = 0;
		double count = xVals.Length;
		 
		for (int ctr = 0; ctr < xVals.Length; ctr++) {
			double x = xVals[ctr];
			double y = yVals[ctr];
			sumCodeviates += x * y;
			sumOfX += x;
			sumOfY += y;
			sumOfXSq += x * x;
			sumOfYSq += y * y;
		}
		ssX = sumOfXSq - ((sumOfX * sumOfX) / count);
		ssY = sumOfYSq - ((sumOfY * sumOfY) / count);
		double RNumerator = (count * sumCodeviates) - (sumOfX * sumOfY);
		double RDenom = (count * sumOfXSq - (sumOfX * sumOfX)) * (count * sumOfYSq - (sumOfY * sumOfY));
		sCo = sumCodeviates - ((sumOfX * sumOfY) / count);
		 
		double meanX = sumOfX / count;
		double meanY = sumOfY / count;
		double dblR = RNumerator / Math.Sqrt(RDenom);
		rsquared = dblR * dblR;
		yintercept = meanY - ((sCo / ssX) * meanX);
		slope = sCo / ssX;
	}
	
	private SendMsg DetermineAction ()
	{
		double myCarAngle = myCar.angle;
		if(myCarAngle < 0){
			myCarAngle = -myCarAngle;
		}
		
		if(myCarAngle > lapMaxAngle){
			lapMaxAngle = myCarAngle;
		}
		
		if(currentGameTick == 0){
			return new Throttle(1.0, currentGameTick);
		}else if(currentGameTick == 1){
			acceleration = myCar.speed; //Get the acceleration without friction
			return new Throttle(1.0, currentGameTick);
		}else if(currentGameTick == 2){
			double a2 = myCar.speed;
			xVals[0] = a2;
			yVals[0] = a2 - acceleration;
			acceleration = a2;
			friction = (2 * acceleration - a2) / Math.Pow(acceleration, 2);
			maxSpeedMultiplier = constant * friction;
			Console.WriteLine("friction: "+friction+" spdmulti: "+maxSpeedMultiplier);
			return new Throttle(1.0, currentGameTick);
		}else if(currentGameTick < 6){ //Method of a 'strong and stupid',collect data and use linear regression to deternime acceleration.
			double a = myCar.speed;
			xVals[currentGameTick - 2] = a;
			yVals[currentGameTick - 2] = a - acceleration;
			acceleration = a;
			return new Throttle(1.0, currentGameTick);
		}else if(currentGameTick == 6){
			LinearRegression();
		}
		
		if(currentTrack.GetNextPiece(myCar).@switch){
			if(!switchSent){
				switchSent = true;
				String switchDir = GetNextSwitch(myCar);
				if(!switchDir.Equals("noswitch")){
					return new SwitchLane(switchDir, currentGameTick);
				}
			}
		}else{
			switchSent = false;
		}
		
		
		if (currentTrack.pieces [myCar.piecePosition.pieceIndex].angle != 0) {
			Console.WriteLine("Car angle: "+myCar.angle);
			//Console.WriteLine("Guessed angle: "+GuessSlipAngle());
			if(myCar.speed > GetMaxEntrySpeed(currentTrack.pieces[myCar.piecePosition.pieceIndex])){
				throttle = 0.0;
			}else{
				double maxTurnVelocity = GetMaxSpeed();
				if(maxTurnVelocity == 0.0){
					throttle = 0.0;
					return new Throttle(throttle, currentGameTick);
				}
				throttle = maxTurnVelocity/10;
				if((myCarAngle < previousAngle + 3) && myCarAngle < 45){
					throttle = 1.0;
				}
				if(myCarAngle > 55 || (myCarAngle > 45 && myCarAngle > previousAngle + 3)){
					throttle = 0.0;
				}
				if(myCarAngle < previousAngle){
					throttle = 1.0;
				}
			}
		} else {
			
			if(turboAvailable){
				if(CanUseTurbo()){
					turboAvailable = false;
					return new UseTurbo(currentGameTick);
				}
			}
			
			Double entryspeed = GetMaxEntrySpeed(currentTrack.pieces[GetNextTurn(myCar)]);
			if(NeedToBreak(entryspeed, currentTrack.pieces[GetNextTurn(myCar)])){
				if(entryspeed < 0){
					throttle = 1.0;
					return new Throttle(throttle, currentGameTick);
				}
				throttle = 0.0;
			}else{
				throttle = 1.0;
			}
		}
		
		if(throttle > 1.0){
			throttle = 1.0;
		}
		
		return new Throttle(throttle, currentGameTick);

	}

	Bot(StreamReader reader, StreamWriter writer, SendMsg join) {
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
				break;
				case "carPositions":
					CarPositions carPositions = JsonConvert.DeserializeObject<CarPositions>(line);
					UpdateCarPositions(carPositions.data);
					currentGameTick = carPositions.gameTick;
					//Console.WriteLine("Car startlaneindex:"+myCar.piecePosition.lane.startLaneIndex+" Car endlaneIndex:"+myCar.piecePosition.lane.endLaneIndex+" Current tick: "+carPositions.gameTick+ " Speed: "+myCar.speed);
					send(DetermineAction());
					previousAngle = myCar.angle;
					if(previousAngle < 0){
						previousAngle = -previousAngle;
					}
					prevThrottle = throttle;
					break;
				case "join":
					Console.WriteLine("Joined");
					break;
				case "crash":
					CrashMsg crashMsg = JsonConvert.DeserializeObject<CrashMsg>(line);
					if(myCar.HasId(crashMsg.data)){ //Houston we have crashed...
						constant = constant * 0.9;
						maxSpeedMultiplier = constant * friction;
						Console.WriteLine("#######CRASH!!##############");
						Console.WriteLine("Speed: "+myCar.speed);
						Console.WriteLine("############################");
					}
					break;
				case "gameInit":
					Console.WriteLine("Race init");
					GameInit gameInit = JsonConvert.DeserializeObject<GameInit>(line);
					currentTrack = CreateTrack(gameInit);
					otherCars = new Cars(gameInit.data.race.cars);
					otherCars.cars.Remove(otherCars.GetCarById(myCar.id));
					break;
				case "gameEnd":
					Console.WriteLine("Race ended");
					break;
				case "gameStart":
					Console.WriteLine("Race starts");
					currentGameTick = 0;
					send(new Ping());
					break;
				case "turboAvailable":
					turboAvailable = true;
					Turbo turbo = JsonConvert.DeserializeObject<Turbo>(line);
					turboDetails = turbo.data;
					currentGameTick = turbo.gameTick;
					break;
				case "lapFinished":
					LapFinished lapFinished = JsonConvert.DeserializeObject<LapFinished>(line);
					if(myCar.HasId(lapFinished.data.car)){
						if(lapMaxAngle < 10){
							constant = constant * 1.4;
							maxSpeedMultiplier = constant * friction;
						}else if(lapMaxAngle < 20){
							constant = constant * 1.3;
							maxSpeedMultiplier = constant * friction;
						}else if(lapMaxAngle < 30){
							constant = constant * 1.2;
							maxSpeedMultiplier = constant * friction;
						}else if(lapMaxAngle < 50){
							constant = constant * 1.1;
							maxSpeedMultiplier = constant * friction;
						}
						Console.WriteLine("Lap finished, new spdmulti: "+maxSpeedMultiplier);
					}
					break;
				default:
					Console.WriteLine(line);
					break;
			}
		}
	}

	private void send(SendMsg msg) {
		writer.WriteLine(msg.ToJson());
		Console.WriteLine(msg.ToJson());
	}
}

class MsgWrapper {
    public string msgType;
    public Object data;
	public int gameTick;

    public MsgWrapper(string msgType, Object data, int gameTick) {
    	this.msgType = msgType;
    	this.data = data;
		this.gameTick = gameTick;
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
/******LAP FINISHED*******/
public class LapTime
{
    public int lap { get; set; }
    public int ticks { get; set; }
    public int millis { get; set; }
}

public class RaceTime
{
    public int laps { get; set; }
    public int ticks { get; set; }
    public int millis { get; set; }
}

public class Ranking
{
    public int overall { get; set; }
    public int fastestLap { get; set; }
}

public class LapData
{
    public Id car { get; set; }
    public LapTime lapTime { get; set; }
    public RaceTime raceTime { get; set; }
    public Ranking ranking { get; set; }
}

public class LapFinished
{
    public string msgType { get; set; }
    public LapData data { get; set; }
    public string gameId { get; set; }
    public int gameTick { get; set; }
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
		this.angularForce = 0.0;
	}
	
    public Id id { get; set; }
    public double angle { get; set; }
    public PiecePosition piecePosition { get; set; }
	public Dimensions dimensions { get; set; }
	public double speed { get; set; }
	public double angularForce { get; set; } 
	
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
			double totalradius = 0;
			if(currentTrack.pieces[this.piecePosition.pieceIndex].angle > 0){
				totalradius = currentTrack.pieces[this.piecePosition.pieceIndex].radius - currentTrack.lanes[this.piecePosition.lane.startLaneIndex].distanceFromCenter;
			}else if(currentTrack.pieces[this.piecePosition.pieceIndex].angle < 0){
				totalradius = currentTrack.pieces[this.piecePosition.pieceIndex].radius + currentTrack.lanes[this.piecePosition.lane.startLaneIndex].distanceFromCenter;
			}

			if(this.piecePosition.pieceIndex == newspeed.piecePosition.pieceIndex){
				this.speed = newspeed.piecePosition.inPieceDistance - this.piecePosition.inPieceDistance;
			}else{
				double previouslength;
				if(currentTrack.pieces[this.piecePosition.pieceIndex].length > 0){
					previouslength = currentTrack.pieces[this.piecePosition.pieceIndex].length;
				}else{
					previouslength = (currentTrack.pieces[this.piecePosition.pieceIndex].angle / 360) * 2 * Math.PI * totalradius;
					if(previouslength < 0){
						previouslength = -previouslength;
					}
				}
				
				this.speed = previouslength - this.piecePosition.inPieceDistance + newspeed.piecePosition.inPieceDistance;
				
			}
			if(totalradius == 0){
				this.angularForce = 0;
			}else{
				this.angularForce = Math.Pow(this.speed, 2) / totalradius;
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

public class TurboDetails
{
    public double turboDurationMilliseconds { get; set; }
    public int turboDurationTicks { get; set; }
    public double turboFactor { get; set; }
}

public class Turbo
{
    public string msgType { get; set; }
    public TurboDetails data { get; set; }
    public int gameTick { get; set; }
}

/******* MESSAGES TO SERVER **********/

abstract class SendMsg {
	public int gameTick { get; set; }
	public string ToJson() {
		return JsonConvert.SerializeObject(new MsgWrapper(this.MsgType(), this.MsgData(), this.gameTick ));
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

	public Throttle(double value, int gameTick) {
		this.value = value;
		this.gameTick = gameTick;
	}

	protected override Object MsgData() {
		return this.value;
	}

	protected override string MsgType() {
		return "throttle";
	}
	
}

class UseTurbo: SendMsg {
	
	public UseTurbo(int gameTick){
		this.gameTick = gameTick;
	}
	
	protected override Object MsgData() {
		return "Kolmosen kautta nelosen puhaltimella...";
	}
	
	protected override string MsgType() {
		return "turbo";
	}
}

class SwitchLane: SendMsg {
	public string lane;

	public SwitchLane(string lane, int gameTick){
		this.lane = lane;
		this.gameTick = gameTick;
	}

	protected override Object MsgData() {
		return this.lane;
	}

	protected override string MsgType(){
		return "switchLane";
	}
}