/*============================================================================== 
 * Copyright (c) 2012-2014 Qualcomm Connected Experiences, Inc. All Rights Reserved. 
 * ==============================================================================*/
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using System.Text.RegularExpressions;
using Vuforia;

/// <summary>
/// A custom event handler for TextReco-events
/// </summary>
public class TextEventHandler : MonoBehaviour, ITextRecoEventHandler, IVideoBackgroundEventHandler
{
    #region PRIVATE_MEMBER_VARIABLES

    private GameObject cube;

    // Size of text search area in percentage of screen
    private bool mutex = false;
    private float mLoupeWidth = 0.9f;
    private float mLoupeHeight = 0.15f;


  //  private bool coroutineActive = false;

    // Alpha value for area outside of text search
    private float mBackgroundAlpha = 0.2f;
    // Size of text box for visualizing detected words in percentage of remaining screen outside text search area
    private float mTextboxWidth = 0.9f;
    private float mTextboxHeight = 0.95f;
    // Number of words before scaling word list
    private int mFixedWordCount = 9;
    // Padding between lines in word list
    private float mWordPadding = 0.05f;
    // Minimum line height for word list
    private float mMinLineHeight = 15.0f;
    // Line width of viusalized boxes around detected words
    private float mBBoxLineWidth = 3.0f;
    // Padding between detected words and visualized boxes
    private float mBBoxPadding = 0.0f;
    // Color of visualized boxes around detected words
    private Color mBBoxColor = new Color(1.0f, 0.447f, 0.0f, 1.0f); // ORANGE BOXES

    private Rect mDetectionAndTrackingRect;
    private Texture2D mBackgroundTexture;
    private Texture2D mBoundingBoxTexture;
    private Material mBoundingBoxMaterial;

    private GUIStyle mWordStyle;
    private bool mIsTablet;
    private bool mIsInitialized;
    private bool mVideoBackgroundChanged;

    private readonly List<WordResult> mSortedWords = new List<WordResult>();
    private Dictionary<string, GameObject> dict = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> dictMeaning = new Dictionary<string, GameObject>();
    // private Dictionary<string, GameObject> dict = new Dictionary<string, GameObject>();

    [SerializeField] 
    private Material boundingBoxMaterial = null;
    #endregion

    #region UNTIY_MONOBEHAVIOUR_METHODS

//111111111111111111111111111111111111111111111111111111111111111111
    public void InitHandler()
    {
        // create the background texture (size 1x1, can be scaled to any size)
        mBackgroundTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        mBackgroundTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, mBackgroundAlpha));
        mBackgroundTexture.Apply(false);

        // create the texture for ORANGE bounding boxes AROUND TEXT DETECTED
        mBoundingBoxTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        mBoundingBoxTexture.SetPixel(0, 0, mBBoxColor);
        mBoundingBoxTexture.Apply(false);

        mBoundingBoxMaterial = new Material(boundingBoxMaterial);
        mBoundingBoxMaterial.SetTexture("_MainTex", mBoundingBoxTexture);

        // words that are printed when detected from screen
        mWordStyle = new GUIStyle();
        mWordStyle.normal.textColor = Color.white;
        mWordStyle.alignment = TextAnchor.UpperCenter;
        mWordStyle.font = Resources.Load("SourceSansPro-Regular_big") as Font;

        

        

        mIsTablet = IsTablet();
        if (QCARRuntimeUtilities.IsPlayMode())
            mIsTablet = false;
        if (mIsTablet)
        {
            mLoupeWidth = 0.6f;
            mLoupeHeight = 0.1f;
            mTextboxWidth = 0.6f;
            mFixedWordCount = 14;
        }

        // register to TextReco events
        var trBehaviour = GetComponent<TextRecoBehaviour>();
        if (trBehaviour)
        {
            trBehaviour.RegisterTextRecoEventHandler(this);
        }

        // register for the OnVideoBackgroundConfigChanged event at the QCARBehaviour
        QCARBehaviour qcarBehaviour = (QCARBehaviour)FindObjectOfType(typeof(QCARBehaviour));
        if (qcarBehaviour)
        {
            qcarBehaviour.RegisterVideoBgEventHandler(this);
        }
    }

    
    public void Draw()
    {
        // draw background - tracking:
        DrawMaskedRectangle(mDetectionAndTrackingRect);
        DrawWordList();
    }

    void OnRenderObject()
    {
        //DrawGameObject();
        DrawWordBoundingBoxes();
        DrawGameObject();
    }
    
    // 222222222222222222222222222222222
    public void UpdateHandler()
    {
        // once the text tracker has initialized and every time the video background changed, set the region of interest
        if (mIsInitialized && mVideoBackgroundChanged)
        {
            TextTracker textTracker = TrackerManager.Instance.GetTracker<TextTracker>();
            if (textTracker != null)
            {
                CalculateLoupeRegion();
                textTracker.SetRegionOfInterest(mDetectionAndTrackingRect, mDetectionAndTrackingRect);
            }
            mVideoBackgroundChanged = false;
            
        }
        
    }
    
    #endregion // UNTIY_MONOBEHAVIOUR_METHODS



    #region ITextRecoEventHandler_IMPLEMENTATION

    /// <summary>
    /// Called when the text reco system has finished initializing
    /// </summary>
    public void OnInitialized()
    {
        CalculateLoupeRegion();
        mIsInitialized = true;
    }

    /// <summary>
    /// This method will be called whenever a new word has been detected
    /// </summary>
    /// <param name="wordResult">New trackable with current pose</param>
    public void OnWordDetected(WordResult wordResult)
    {
        var word = wordResult.Word;
        //Debug.LogError(word.position);
        if (ContainsWord(word))
            Debug.LogError("Word was already detected before!");


        Debug.Log("Text: New word: " + wordResult.Word.StringValue + "(" + wordResult.Word.ID + ")");
        AddWord(wordResult);
    }

    /// <summary>
    /// This method will be called whenever a tracked word has been lost and is not tracked anymore
    /// </summary>
    public void OnWordLost(Word word)
    {
        if (!ContainsWord(word))
            Debug.LogError("Non-existing word was lost!");

        Debug.Log("Text: Lost word: " + word.StringValue + "(" + word.ID + ")");

        RemoveWord(word);
    }

    #endregion // PUBLIC_METHODS 


    
    #region IVideoBackgroundEventHandler_IMPLEMENTATION
    
    // set a flag that the video background has changed. This means the region of interest has to be set again.
    public void OnVideoBackgroundConfigChanged()
    {
        mVideoBackgroundChanged = true;
    }

    #endregion // IVideoBackgroundEventHandler_IMPLEMENTATION



    #region PRIVATE_METHODS

    /// <summary>
    /// Draw a 3d bounding box around each currently tracked word
    /// </summary>
    private void DrawWordBoundingBoxes()
    {
        // render a quad around each currently tracked word
        foreach (var word in mSortedWords)
        {
            var pos = word.Position;
            var orientation = word.Orientation;
            var size = word.Word.Size;
            var pose = Matrix4x4.TRS(pos, orientation, new Vector3(size.x, 1, size.y));

            var cornersObject = new[]
                {
                    new Vector3(-0.5f, 0.0f, -0.5f), new Vector3(0.5f, 0.0f, -0.5f),
                    new Vector3(0.5f, 0.0f, 0.5f), new Vector3(-0.5f, 0.0f, 0.5f)
                };
            var corners = new Vector2[cornersObject.Length];
            for (int i = 0; i < cornersObject.Length; i++)
                corners[i] = Camera.current.WorldToScreenPoint(pose.MultiplyPoint(cornersObject[i]));
            DrawBoundingBox(corners);
        }
    }

    private IEnumerator LoadImage(string link, string text) {
        WWW www = new WWW(link);

        yield return www;

        cube = GameObject.CreatePrimitive(PrimitiveType.Plane); 
        cube.GetComponent<Renderer>().material.mainTexture = www.texture;
        // dict.Add(text, cube);
        dict[text] = cube;
        mutex = false;
    }

    private IEnumerator LoadImageMeaning(string word, string text) {
        word = word.Replace(" ", "%20");
        string link = "http://api.img4me.com/?text="+ word +"&font=arial&fcolor=000000&size=20&bcolor=FFFFFF&type=jpg";
        Debug.Log("link -> " + link);
        WWW www = new WWW(link);

        yield return www;
        Debug.Log("generated image -> " + www.text);
        www = new WWW(www.text);

        yield return www;

        cube = GameObject.CreatePrimitive(PrimitiveType.Plane); 
        cube.GetComponent<Renderer>().material.mainTexture = www.texture;
        // dict.Add(text, cube);
        dictMeaning[text] = cube;
        mutex = false;
    }

    IEnumerator pleaseWait(int sec)
    {
        print(Time.time);
        yield return new WaitForSeconds(5);
        print(Time.time);
    }

    private IEnumerator DoWWW(string text)
    {
       // coroutineActive = true;
        mutex = true;
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Plane);;
        dict.Add(text, temp);
        var url = "http://images.google.com/images?q="+ text +"&source=lnms&tbm=isch&sa=X&ved=0ahUKEwi3_YHc_bXQAhXEuI8KHbiRCtwQ_AUICygE&biw=1366&bih=550";
       // var url = "http://images.google.com/images?q="+ text +"&hl=en&imgsz=Medium";
       // var url = "https://en.wikipedia.org/wiki/Main_Page";
        StartMyDownload(url, text);

        yield return 0;
       /* WWW www = new WWW(url);
        
        string returnStr;
        
        //yield return StartCoroutine(pleaseWait(2));
        
         //Debug.Log(www.text); 

        print(Time.time);
        yield return new WaitForSeconds(5);
        print(Time.time);

       // while(www.text.Contains("imgrefurl"));

        Debug.Log(www.text); 

        print(Time.time);
        yield return new WaitForSeconds(5);
        print(Time.time);

        Debug.Log(www.text); 

        

        yield return www;
        returnStr = www.text;

        Debug.Log(returnStr);    
       
        var startIndex = returnStr.IndexOf("imgres?imgurl=");
        returnStr = returnStr.Substring(startIndex+14);
        Debug.Log(returnStr);
        var endIndex = returnStr.IndexOf("imgrefurl");
        returnStr = returnStr.Substring(0,endIndex-1);
        Debug.Log(returnStr);
        
        yield return LoadImage(returnStr);
*/
        
       // Debug.Log(" meaning -> " + www.text);
        
    }

    private IEnumerator DoWordWWW(string text)
    {
       // coroutineActive = true;
        mutex = true;
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Plane);;
        dictMeaning.Add(text, temp);
        var url = "http://www.yourdictionary.com/"+ text;
       // var url = "http://images.google.com/images?q="+ text +"&hl=en&imgsz=Medium";
       // var url = "https://en.wikipedia.org/wiki/Main_Page";
        StartMyDownload(url, text);

        yield return 0;
    }

    public  void StartMyDownload(string sURL, string text)
    {
        try
        {  
            //DownloadManager dm = new DownloadManager();
            DownloadManager.Download(sURL, DownloadCallback, text);                  
        }
        catch(Exception e)
        {
            Debug.Log(e.ToString());
        }
    }
   
    public  void DownloadCallback(string data, string sError, string text)
    {
        try
        {  
            if( null != sError )
                Debug.Log(sError);
            else {

                //----------------------------------------------------------------------
                    
                    if(Regex.IsMatch(data, @"\bhttp://t0\.gstatic\.com\S*")) {
                        Match mc = Regex.Match(data, @"\bhttp://t0\.gstatic\.com\S*");
                        
                        string str = mc + "";
                        str = str.Substring(0, str.Length - 1);
                        Debug.Log("link found -> " + str);

                    
                        Debug.Log("FFFFFFFFfinal MOST response -> " + data);    
                        StartCoroutine(LoadImage(str, text));
                    }
                    
                //-----------------------------------------------------------------
                    if(Regex.IsMatch(data, @"\bclass=\Scustom_entry\S>.*\.<")) {

                        Debug.Log("MEEAANNNIIIIINNNNGGGG -> " + data);

                        Match mc = Regex.Match(data, @"\bclass=\Scustom_entry\S>(.*?)</div>");
                    
                        Debug.Log("MEEAANNNIIIIINNNNGGGG -> " + data); 
                        string str = mc + "";
                        Debug.Log(str);
                        str = str.Substring(21, str.Length - 6 - 21);
                        Debug.Log(str);
                        StartCoroutine(LoadImageMeaning(str, text));
                    }
                    
                
            }                 
        }
        catch(Exception e)
        {
            Debug.Log(e.ToString());
        }
       
    }  
 




    private void DrawGameObject() {
        foreach (var word in mSortedWords)
        {
            var pos = word.Position;
            string str = word.Word.StringValue;
           // pos.z = pos.z + word.Word.Size.x*3;
          //  float size = word.Word.Size.x;
            if(!dict.ContainsKey(str) || !dictMeaning.ContainsKey(str)) {
                if(mutex == false && !dict.ContainsKey(str)) {
                    Debug.Log("word searched -> " + str);
                    StartCoroutine(DoWWW(str));
                }
                else if(mutex == false && !dictMeaning.ContainsKey(str)) {
                    Debug.Log("word meaning searched -> " + str);
                    StartCoroutine(DoWordWWW(str));    
                }
            //    break;
            }
            else {
                if(dict[str]){
                    GameObject cubeClone = Instantiate( dict[str] ) as GameObject;
                    
                    cubeClone.transform.localScale = new Vector3(10f, 10f, 10f);
                    cubeClone.transform.RotateAround(transform.position, transform.up, 180f);
                    Debug.Log("size -> " + cubeClone.GetComponent<Renderer>().bounds.size.x);
                    float imageSize = cubeClone.GetComponent<Renderer>().bounds.size.x;
                    pos.z -= imageSize;
                    cubeClone.transform.position = pos;
                    Destroy(cubeClone, 0.1f);
                }
                
                if(dictMeaning[str]) {
                    GameObject cubeClone = Instantiate( dictMeaning[str] ) as GameObject;
                   // float ratio = cubeClone.GetComponent<Renderer>().bounds.size.x / cubeClone.GetComponent<Renderer>().bounds.size.y;
                    
                    cubeClone.transform.localScale = new Vector3(25f, 8f, 5f);
                    cubeClone.transform.RotateAround(transform.position, transform.up, 180f);
                    Debug.Log("size -> " + cubeClone.GetComponent<Renderer>().bounds.size.x);
                    float imageSize = cubeClone.GetComponent<Renderer>().bounds.size.x;
                  //  pos.z -= imageSize;
                    pos.x += imageSize;
                    cubeClone.transform.position = pos;
                    Destroy(cubeClone, 0.1f);  
                }
            } 
            

            break;
            // else
            //     break;

          //  GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            
        }
    }

    /// <summary>
    /// Print string values for all currently tracked words.
    /// </summary>
    private void DrawWordList()
    {
        var sortedWords = mSortedWords;

        var textBoxWidth = Screen.width * mTextboxWidth;
        var textBoxHeight = (Screen.height - mDetectionAndTrackingRect.yMax) * mTextboxHeight;
        var textBoxOffsetLeft = (Screen.width - textBoxWidth) * 0.5f;
        var textBoxOffsetTop = mDetectionAndTrackingRect.yMax + (Screen.height - (textBoxHeight + mDetectionAndTrackingRect.yMax)) * 0.5f;

        var textBox = new Rect(textBoxOffsetLeft, textBoxOffsetTop, textBoxWidth, textBoxHeight);
        Rect wordBox;
        var scale = ComputeScaleForWordList(mSortedWords.Count, textBox, out wordBox);

        var oldMatrix = GUI.matrix;

        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(Screen.width * 0.5f, textBoxOffsetTop));

        wordBox.y += wordBox.height*mWordPadding;
        foreach (var word in sortedWords)
        {
            if ((wordBox.yMax - textBoxOffsetTop) * scale > textBox.height)
                break;
           // GUI.Label(wordBox, word.Word.StringValue, mWordStyle);
            GUI.Label(wordBox, word.Position.x + " x, " +  word.Position.y + " y, " +  word.Position.z + " z ", mWordStyle);
            wordBox.y += (wordBox.height + wordBox.height * mWordPadding);
        }

        GUI.matrix = oldMatrix;
    }

    private void DrawMaskedRectangle(Rect rectangle)
    {
        // draw four texture quads in UI that mask out the given region
        //GUI.DrawTexture(rectange, texture);
        //new Rect(xupperleft pos, y upper left coordinate, width x right, widht y down);

        GUI.DrawTexture(new Rect(0f, 0f, rectangle.xMin, Screen.height), mBackgroundTexture);
        GUI.DrawTexture(new Rect(rectangle.xMin, 0f, rectangle.width, rectangle.yMin), mBackgroundTexture);
        GUI.DrawTexture(new Rect(rectangle.xMin, rectangle.yMax, rectangle.width, Screen.height - rectangle.yMax), mBackgroundTexture);
        GUI.DrawTexture(new Rect(rectangle.xMax, 0f, Screen.width - rectangle.xMax, Screen.height), mBackgroundTexture);
    }

    private void DrawBoundingBox(Vector2[] corners)
    {
        var normals = new Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            var p0 = corners[i];
            var p1 = corners[(i + 1)%4];
            normals[i] = (p1 - p0).normalized;
            normals[i] = new Vector2(normals[i].y, -normals[i].x);
        }

        //add padding to inner corners
        corners = ExtendCorners(corners, normals, mBBoxPadding);
        //computer outer corners
        var outerCorners = ExtendCorners(corners, normals, mBBoxLineWidth);

        //create vertices in screen space
        var vertices = new Vector3[8];
        for (var i = 0; i < 4; i++)
        {
            vertices[i] = new Vector3(corners[i].x, corners[i].y, Camera.current.nearClipPlane);
            vertices[i + 4] = new Vector3(outerCorners[i].x, outerCorners[i].y, Camera.current.nearClipPlane);
        }
        //transform vertices into world space
        for (int i = 0; i < 8; i++)
            vertices[i] = Camera.current.ScreenToWorldPoint(vertices[i]);

        var mesh = new Mesh()
            {
                vertices = vertices,
                uv = new Vector2[8],
                triangles = new[]
                    {
                        0, 5, 4, 1, 5, 0,
                        1, 6, 5, 2, 6, 1,
                        2, 7, 6, 3, 7, 2,
                        3, 4, 7, 0, 4, 3
                    },
            };

        mBoundingBoxMaterial.SetPass(0);
        Graphics.DrawMeshNow(mesh, Matrix4x4.identity);

        // GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        // cube.transform.position = pos;
        // cube.transform.localScale = new Vector3(size*3,
        //                                              1f,size*3);
        // Destroy(cube, 0.2f);
        Destroy(mesh);
    }


    private static Vector2[] ExtendCorners(Vector2[] corners, Vector2[] normals, float extension)
    {
        //compute positions along the outer side of the boundary
        var linePoints = new Vector2[corners.Length * 2];
        for (var i = 0; i < corners.Length; i++)
        {
            var p0 = corners[i];
            var p1 = corners[(i + 1) % 4];

            var po0 = p0 + normals[i] * extension;
            var po1 = p1 + normals[i] * extension;
            linePoints[i * 2] = po0;
            linePoints[i * 2 + 1] = po1;
        }

        //compute corners of outer side of bounding box lines
        var outerCorners = new Vector2[corners.Length];
        for (var i = 0; i < corners.Length; i++)
        {
            var i2 = i * 2;
            outerCorners[(i + 1) % 4] = IntersectLines(linePoints[i2], linePoints[i2 + 1], linePoints[(i2 + 2) % 8],
                                             linePoints[(i2 + 3) % 8]);
        }
        return outerCorners;
    }

    /// <summary>
    /// Intersect the line p1-p2 with the line p3-p4
    /// </summary>
    private static Vector2 IntersectLines(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        var denom = (p1.x - p2.x) * (p3.y - p4.y) - (p1.y - p2.y) * (p3.x - p4.x);
        var x = ((p1.x * p2.y - p1.y * p2.x) * (p3.x - p4.x) - (p1.x - p2.x) * (p3.x * p4.y - p3.y * p4.x)) / denom;
        var y = ((p1.x * p2.y - p1.y * p2.x) * (p3.y - p4.y) - (p1.y - p2.y) * (p3.x * p4.y - p3.y * p4.x)) / denom;
        return new Vector2(x, y);
    }

//============================================================================================================================================







    private static bool IsTablet()
    {
#if (UNITY_IPHONE || UNITY_IOS)
#if (UNITY_5_0 || UNITY_5_1)
        return UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad1Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad2Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad3Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad4Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini1Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini2Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini3Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadAir1 ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadAir2 ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadUnknown;
#else
        return iPhone.generation == iPhoneGeneration.iPad1Gen ||
               iPhone.generation == iPhoneGeneration.iPad2Gen ||
               iPhone.generation == iPhoneGeneration.iPad3Gen ||
               iPhone.generation == iPhoneGeneration.iPad4Gen ||
               iPhone.generation == iPhoneGeneration.iPadMini1Gen ||
               iPhone.generation == iPhoneGeneration.iPadUnknown;
#endif// endif UNITY_5
#else

        var screenWidth = Screen.width / Screen.dpi;
        var screenHeight = Screen.height / Screen.dpi;
        var diagonal = Mathf.Sqrt(Mathf.Pow(screenWidth, 2) + Mathf.Pow(screenHeight, 2));
        //tablets usually have a screen size greater than 6 inches
        return diagonal >= 6;
#endif

    }

    /// <summary>
    /// compute the scale for printing the string values of the currently tracked words
    /// </summary>
    /// <param name="numWords">Number of currently tracked words</param>
    /// <param name="totalArea">Region where all words should be printed</param>
    /// <param name="firstWord">Region for first word</param>
    /// <returns>necessary scale to put all words into the area</returns>
    private float ComputeScaleForWordList(int numWords, Rect totalArea, out Rect firstWord)
    {
        if (numWords < mFixedWordCount)
            numWords = mFixedWordCount;
        
        
        var originalHeight = mWordStyle.lineHeight;
        var requestedHeight = totalArea.height / (numWords + mWordPadding * (numWords + 1));

        if (requestedHeight < mMinLineHeight)
        {
            requestedHeight = mMinLineHeight;
        }
        var scale = requestedHeight / originalHeight;

        var newWidth = totalArea.width / scale;
        firstWord = new Rect(totalArea.xMin + (totalArea.width - newWidth) * 0.5f, totalArea.yMin, newWidth, originalHeight);
        return scale;
    }


    private void AddWord(WordResult wordResult)
    {
        //add new word into sorted list
        var cmp = new ObbComparison();
        int i = 0;
        while (i < mSortedWords.Count && cmp.Compare(mSortedWords[i], wordResult) < 0)
        {
            i++;
        }

        if (i < mSortedWords.Count)
        {
            mSortedWords.Insert(i, wordResult);
        }
        else
        {
            mSortedWords.Add(wordResult);
        }
    }

    private void RemoveWord(Word word)
    {
        for (int i = 0; i < mSortedWords.Count; i++)
        {
            if (mSortedWords[i].Word.ID == word.ID)
            {
                mSortedWords.RemoveAt(i);
                break;
            }
        }
    }

    private bool ContainsWord(Word word)
    {
        foreach (var w in mSortedWords)
            if (w.Word.ID == word.ID)
                return true;
        return false;
    }

    private void CalculateLoupeRegion()
    {
        // define area for text search
        var loupeWidth = mLoupeWidth * Screen.width;
        var loupeHeight = mLoupeHeight * Screen.height;
        var leftOffset = (Screen.width - loupeWidth) * 0.5f;
        var topOffset = leftOffset;
        mDetectionAndTrackingRect = new Rect(leftOffset, topOffset, loupeWidth, loupeHeight);
    }

    
    #endregion //PRIVATE_METHODS
}

