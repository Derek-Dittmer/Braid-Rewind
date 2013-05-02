using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class s_Rewindable : MonoBehaviour
{
	class LastPos
	{
		int mTimeStamp;
		short mXPos;
		short mYPos;
		short mYVel;
		
		public void Set(int aTimeStamp, short aXPos, short aYPos, short aYVel)
		{
			mTimeStamp = aTimeStamp;
			mXPos = aXPos;
			mYPos = aYPos;
			mYVel = aYVel;
		}
		
		public int GetTimeStamp()	{ return mTimeStamp; }
		public short GetXPos()		{ return mXPos; }
		public short GetYPos()		{ return mYPos; }
		public short GetYVel()		{ return mYVel; }
	}
	
	public static int mSaveFloatPrecision = 1000; 	//Precision of saved float. 100 allows for map size of about -300 to 300. 1000 allows about -30 to 30.				//Extra precision for the saved delta, because they are such small numbers
	[System.NonSerialized][HideInInspector]
	public int mRecordFrame = 6;//Every nth frame that is recorded. Public so each can set it
	
	const float mDeltaTime = (1.0f/s_TimeManager.sFrameRate); //Used for lerping instead of using Time.deltaTime (was causing issues)
	
	List<LastPos> mActions;		//List of last positions
	int mActionItr = 1;			//When rewinding, this counts down from end.
	int mCurrentFrame = 0;		//What the current frame is
	Vector3 mStartPos;			//When rewinding, where you are lerping from	
	Vector3 mToPos;				//When rewinding, where you are lerping to
	float mStartTime = 0;		//Start time when lerping to an mAction
	float mRewindSpeed = 0;		//Tells it how fast to lerp using the mAction's time stamps
	float mPosTol  = 0.01f;		//Tolerance for checking if you have moved, and therefore position will be saved
	
	float mCurXPos = 0;			//Checks to see if you have moved. Won't save if you dont move.
	float mPreXPos = 0;			//^^
	float mCurYPos = 0;			//^^
	float mPreYPos = 0;			//^^
	
	s_PhysicsBase mPhysics = null;//If you have physics, adds force when rewind ends
	
	bool mSnapToAction = false;	//Will set your position to the last actions position before moving to the next
	bool mWasGrounded = true;	//For checking if you were on the ground, but no longer are
	
	void Start()
	{	
		mStartTime = Time.deltaTime;
		mActions = new List<LastPos>();
		mToPos = transform.position; //If this isn't set to something, will go to 0, 0, 0 I found out.
		
		if (GetComponent<s_PhysicsBase>() != null)
			mPhysics = GetComponent<s_PhysicsBase>();
		
		//Add start location
		AddCurrentPosition();

		s_TimeManager.Get.AddStartFunction(RewindStart);
		s_TimeManager.Get.AddEndFunction(RewindEnd);
		s_TimeManager.Get.AddChangeFunction(RewindSpeedChange);
	}
	
	void Update()
	{
		if (s_TimeManager.sRewind)
			Rewind();
		else
			Forward();
	}
	
	void Rewind()
	{
		//While loop, for when there are multiple actions to skip over. Especially when rewing at high speeds
		while (s_TimeManager.sRewindSpeed < 0 && mActions[mActions.Count-mActionItr].GetTimeStamp() >= s_TimeManager.sTimeStamp
			|| s_TimeManager.sRewindSpeed > 0 && mActions[mActions.Count-mActionItr].GetTimeStamp() <= s_TimeManager.sTimeStamp)
		{	
			//Check if you should go to next point depending on if time is going forward or backward
			if (s_TimeManager.sRewindSpeed < 0 && mActionItr < mActions.Count
			|| s_TimeManager.sRewindSpeed > 0 && mActionItr > 1)
				SetNextPosition();
			else
				break;
		}
		
		//Move! Speed/direction is based on rewind speed
		if (mRewindSpeed != 0 && Time.timeScale != 0)
			transform.position = Vector3.Lerp(mStartPos, mToPos, (Time.time - mStartTime) / (mRewindSpeed / -s_TimeManager.sRewindSpeed));
	}
	
	void Forward()
	{	
		mRewindSpeed = 0; //Reset rewind
		
		CheckSaveSpecial();
		
		mPreXPos = mCurXPos;
		mPreYPos = mCurYPos;
		mCurXPos = transform.position.x;
		mCurYPos = transform.position.y;
		
		//Only record every couple of frames, and only if you aren't moving
		if (mCurrentFrame == mRecordFrame)
		{
			if(mCurXPos - mPreXPos > mPosTol || mCurXPos - mPreXPos < -mPosTol
			|| mCurYPos - mPreYPos > mPosTol || mCurYPos - mPreYPos < -mPosTol)
				AddCurrentPosition();
		}
		else
			++mCurrentFrame;
	}
		
	void SetNextPosition()
	{	
		if (mSnapToAction)
			transform.position = new Vector3(s_Utils.ShortToFloat(mActions[mActions.Count-mActionItr].GetXPos()),
											s_Utils.ShortToFloat(mActions[mActions.Count-mActionItr].GetYPos()),
											0);
		mSnapToAction = true;
		
		//Set next (or previous) point
		if (s_TimeManager.sRewindSpeed < 0 && mActionItr < mActions.Count)
			++mActionItr;
		else if (s_TimeManager.sRewindSpeed > 0 && mActionItr > 1)
			--mActionItr;
		
		//Initialize next point
		mRewindSpeed = (s_TimeManager.sTimeStamp - mActions[mActions.Count-mActionItr].GetTimeStamp());
		mRewindSpeed *= mDeltaTime; //Using Time.deltaTime caused weird lepring issues if framerate changed. This looks a bit better
		mToPos = new Vector3(s_Utils.ShortToFloat(mActions[mActions.Count-mActionItr].GetXPos()), s_Utils.ShortToFloat(mActions[mActions.Count-mActionItr].GetYPos()), 0);
		mStartPos = transform.position;
		mStartTime = Time.time;
	}
	
	//Public because there are some moments that need to be saved for different things (like when hitting the ground)
	public void AddCurrentPosition()
	{		
		float vel = 0;
		float x = transform.position.x;
		float y = transform.position.y;
		
		//Check if this is your first save
		if (mActions.Count > 0)
		{
			//Don't want to save the same position as last ones
			if(s_Utils.ShortToFloat(s_Utils.FloatToShort(x)) != s_Utils.ShortToFloat(mActions[mActions.Count-1].GetXPos())
			|| s_Utils.ShortToFloat(s_Utils.FloatToShort(y)) != s_Utils.ShortToFloat(mActions[mActions.Count-1].GetYPos()))
			{
				//Final check to make sure time stamp didn't get messed up
				if (mActions[mActions.Count-1].GetTimeStamp() < s_TimeManager.sTimeStamp)
				{
					//Get velocity depending on if you have physics
					if (mPhysics != null)
						vel = mPhysics.mMoveVector.y;
					
					//Add the position
					LastPos temp = new LastPos();
					temp.Set(s_TimeManager.sTimeStamp, s_Utils.FloatToShort(x), s_Utils.FloatToShort(y), s_Utils.FloatToShort(vel));
					mActions.Add(temp);
				}
			}
		}
		else
		{
			//Add the first position with no velocity
			LastPos temp = new LastPos();
			temp.Set(s_TimeManager.sTimeStamp, s_Utils.FloatToShort(x), s_Utils.FloatToShort(y), s_Utils.FloatToShort(vel));
			mActions.Add(temp);
		}
		
		mCurrentFrame = 0; //Reset it in here, because other things can call this function
	}
	
	void CheckSaveSpecial()
	{
		if (mPhysics != null)
		{
			//Make sure it saves your position when you hit the floor, so you don't float
			if (mPhysics.IsGrounded(gameObject) && !mWasGrounded)
			{
				mWasGrounded = true;
				AddCurrentPosition();
			}
			
			//Save the position where you just get off the ground
			if (!mPhysics.IsGrounded(gameObject) && mWasGrounded)
			{
				mWasGrounded = false;
				AddCurrentPosition();
			}
		}
	}
	
	//The TimeManager calls these functions
	public void RewindStart()
	{
		//Add this pos here so you can go the the exact spot when rewinding your rewind
		AddCurrentPosition();
	}
	
	public void RewindEnd()
	{	
		//Remove rewinded positions
		mActions.RemoveRange((mActions.Count - mActionItr+1), mActionItr-1);
		mActionItr = 1;
		
		float yMoveDir = s_Utils.ShortToFloat(mActions[mActions.Count-1].GetYVel());
		Debug.Log(yMoveDir);
		//Re apply vertical velocity for physics objects
		if (mPhysics && yMoveDir != 0 && yMoveDir != -1) //Player's is -1 because of how his collision works
				mPhysics.AddVerticalVelocity(yMoveDir, s_TimeManager.sTimeStamp - mActions[mActions.Count-1].GetTimeStamp());
		
		//Important to add your current position right after rewind stops
		AddCurrentPosition();
	}
	
	public void RewindSpeedChange()
	{
		//Change your movement speed at this moment, rather than wait until you get to the next point
		mSnapToAction = false;
		
		mRewindSpeed = (s_TimeManager.sTimeStamp - mActions[mActions.Count-mActionItr].GetTimeStamp());
		mRewindSpeed *= mDeltaTime;
		mStartTime = Time.time;
		mStartPos = transform.position;
	}
	
}