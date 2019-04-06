﻿using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

public class BKGGenerator
{
    private float lat;
    private float lon;
    private int rad;
    private int level;
    private float unit;

    private int nn = 0;

    private static string url3 = "http://xdworld.vworld.kr:8080/XDServer/requestLayerNode?APIKey=";
    private static string apiKey = "43247F3D-DCBC-3A57-91FE-D8959E540D2C";

    //중복 다운이나 변환하지 않도록 저장할 폴더
    private static string storageDirectory = Application.dataPath + "\\resource\\vworld_terrain\\";

    //얻고자 하는 영역을 그때그때 다르게 설정해주면 좋다. obj파일들만 저장됨
    private static string targetDirectory = Application.dataPath + "\\resource\\vworld\\#obj_test\\";

    private static double WGS84_EQUATORIAL_RADIUS = 6378137.0; // ellipsoid equatorial getRadius, in meters
    private static double WGS84_POLAR_RADIUS = 6356752.3; // ellipsoid polar getRadius, in meters
    private static double WGS84_ES = 0.00669437999013; // eccentricity squared, semi-major axis / 이심률 제곱 / 이심률 = Math.sqrt(1-(장반경제곱/단반경제곱))

    private static double ELEVATION_MIN = -11000d; // Depth of Marianas trench
    private static double ELEVATION_MAX = 8500d; // Height of Mt. Everest.
    private static int fileSize;

    public BKGGenerator(float lat, float lon, int rad, int level)
    {
        this.lat = lat;
        this.lon = lon;
        this.rad = rad;
        this.level = level;
        unit = 360 / (Mathf.Pow(2, level) * 10); //15레벨의 격자 크기(단위:경위도)
    }

    // Use this for initialization
    void Generate()
    {
        //필요한 subfolder를 만든다. 이미 있으면 건너뛴다.
        string[] folders1 = { "DEM bil", "DEM txt_Cartesian", "DEM txt_latlon", "DEM txt_UTMK", "DEM dds" };
        MakeSubFolders(storageDirectory, folders1);
        string[] folders2 = { "DEM obj", "DEM obj_UTMK" };
        MakeSubFolders(targetDirectory, folders2);

        string layerName = "dem";
        string layerName2 = "tile";

        float minLon = lon - (float)rad / 111000; //경도
        float minLat = lat - (float)rad / 88000; //위도	 
        float maxLon = lon + (float)rad / 111000;
        float maxLat = lat + (float)rad / 88000;

        //idx와 idy를 받는 1단계 단계를 생략하고 여기서 직접 계산한다.
        int minIdx = (int)Mathf.Floor(minLon + 180 / unit);
        int minIdy = (int)Mathf.Floor(minLat + 90 / unit);
        int maxIdx = (int)Mathf.Floor(maxLon + 180 / unit);
        int maxIdy = (int)Mathf.Floor(maxLat + 90 / unit);

        //중복 다운로드를 피하기 위해 현재 있는 파일들 목록을 구한다.
        List<string> fileExistBil = GetFileNames(storageDirectory + "DEM bil\\", ".bil");
        List<string> fileExistTxt = GetFileNames(storageDirectory + "DEM txt_latlon\\", ".txt");
        List<string> fileNamesDds = GetFileNames(storageDirectory + "DEM dds\\", ".dds");

        int listLength = maxIdx * maxIdy;

        //단위 구역들을 차례차례 처리한다.
        for (int i = minIdx; i <= maxIdx; i++)
        {
            for (int j = minIdy; j <= maxIdy; j++)
            {
                Debug.Log("file :" + i + "_" + j + "세션 시작....." + (i + 1) + "/" + i * j);

                //tile 이미지를 받아온다.
                string fileNameDds = "tile_" + i + "_" + j + ".dds";

                if (!fileNamesDds.Contains(fileNameDds))
                {
                    string address3_1 = url3 + apiKey + "&Layer=" + layerName2 + "&Level=" + level + "&IDX=" + i + "&IDY=" + j;
                    RequestFile(address3_1, "DEM dds\\" + fileNameDds);
                }
                Debug.Log("tile ok");

                //만약 이미 bil 파일이 존재하면 건너뛴다.
                string fileNameBil = "terrain file_" + i + "_" + j + ".bil";
                if (!fileExistBil.Contains(fileNameBil))
                {
                    //존재하지 않으면 다운받는다.				
                    string address3 = url3 + apiKey + "&Layer=" + layerName + "&Level=" + level + "&IDX=" + i + "&IDY=" + j;
                    int size = RequestFile(address3, "DEM bil\\" + fileNameBil);   //IDX와 IDY 및 nodeLevel을 보내서 bil목록들을 받아 bil에 저장한다.
                    if (size < 16900)
                    { //제대로 된 파일이 아니면.
                        Debug.Log("file :" + i + "_" + j + "세션 건너뜀(용량부족)....." + (i + 1) + "/" + listLength);
                        continue;
                    }
                }

                string fileNameParsedTxt = "terrain file_" + i + "_" + j + ".txt";
                if (!fileExistTxt.Contains(fileNameParsedTxt))
                {
                    BilParser(fileNameBil); //dat를 다시 읽고 txt에 파싱한다.
                }

                string fileNameObj = "obj file_" + i + "_" + j + ".obj";


                Debug.Log(fileNameParsedTxt + "저장완료....." + (i + 1) + "/" + listLength);
            }
        }
    }

    private void MakeSubFolders(string fileLocation, string[] subfolders)
    {
        string[] files = Directory.GetDirectories(fileLocation);
        foreach (string subfolder in subfolders)
        {
            bool isExist = false;
            if (files != null)
            {
                for (int j = 0; j < files.Length; j++)
                {
                    if (files[j].Equals(subfolder))
                    {
                        isExist = true; //같은 이름이 있으면 빠져나오고
                        break;
                    }
                } //for
            }
            if (!isExist)
            {
                DirectoryInfo newDir = Directory.CreateDirectory(fileLocation + subfolder);
                newDir.Create();
            }
        }
    }

    private List<string> GetFileNames(string fileLocation, string extension)
    {
        List<string> fileNames = new List<string>();
        string[] files = Directory.GetFiles(fileLocation);

        // 디렉토리가 비어 있지 않다면 
        if (!(files.Length <= 0))
        {
            for (int i = 0; i < files.Length; i++)
            {
                if (files[i].EndsWith(extension))
                {
                    fileNames.Add(files[i]);
                }
            }
        }
        return fileNames;
    }

    private void BilParser(string fileName)
    {
        FileStream inFileStream = File.OpenRead(storageDirectory + "DEM bil\\" + fileName);
        using (BinaryReader inputStream = new BinaryReader(inFileStream))
        {
            //terrain height
            //vworld에서 제공하는 DEM이 65x65개의 점으로 되어 있다.
            for (int ny = 64; ny >= 0; ny--)
            {
                for (int nx = 0; nx < 65; nx++)
                {
                    float height = inputStream.ReadSingle();
                    //65x65의 격자이지만 후속 작업을 고려하여 일렬로 기록한다.
                    Debug.Log("" + height);
                    
                }
            }
        }
    }

    /*
	 * httpRequest를 보내고 바이너리 파일을 받아 저장한다.
	 * @param address
	 * @param fileName
	 */
    private static async Task RequestFile(string address, string fileName)
    {
        long size = 0;

        string url = address;  //테스트 사이트
        string responseText = string.Empty;

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Timeout = 30 * 1000; // 30초
        request.Headers.Add("Referer", "http://localhost:4141");
        
        using (HttpWebResponse resp = (HttpWebResponse)request.GetResponse())
        {
            HttpStatusCode status = resp.StatusCode;
            Debug.Log(status);  // 정상이면 "OK"

            using (Stream respStream = resp.GetResponseStream())
            {
                using (FileStream fileStream = File.OpenWrite(storageDirectory + fileName))
                {
                    respStream.CopyToAsync(fileStream);
                    size = respStream.Length;
                }
            }
        }
        if (size == 0)
            Debug.Log(fileName + " Request Failed.");
        else
            Debug.Log(fileName + " Successfully Saved");
    }
}
