namespace OCR.Core

open Tesseract
open System
open System.Drawing
open System.Drawing.Imaging
open System.IO
open Emgu
open Emgu.CV
open Emgu.CV.CvEnum
open Emgu.CV.Structure
open Emgu.CV.UI
open Emgu.Util
open ImageMagick
open MongoDB.Bson 
open MongoDB.Driver
open System.Configuration


type TextData = {Id: BsonObjectId; Text: String;}


//MongoDB manipulation module
module dbConnector =
    
    [<Literal>]
    let connectionString = "mongodb://localhost:27017/"

    [<Literal>]
    let dbName = "test"

    [<Literal>]
    let collectionName = "ocr"

    type TextData = {Text: String;}

    let client = MongoClient(connectionString)
    let db     = client.GetDatabase(dbName)
    let coll   = db.GetCollection<TextData>(collectionName)


    let create(text : TextData) = 
        coll.InsertOne(text)

    let readAll = 
        coll.Find(Builders.Filter.Empty).ToEnumerable()


//Image manipulation module  
module ImgMan = 

    let filterBitmap(image:Bitmap, f:int[] -> int[]) =

        let Width = image.Width
        let Height = image.Height

        //Grab original data 
        let rect = new Rectangle(0,0,Width,Height)
        let data = image.LockBits(rect, ImageLockMode.ReadOnly, image.PixelFormat)

        // Copy the data
        let ptr = data.Scan0
        let stride = data.Stride
        let bytes = stride * data.Height
        let values : byte[] = Array.zeroCreate bytes
        System.Runtime.InteropServices.Marshal.Copy(ptr,values,0,bytes)
        image.UnlockBits(data)
    
        let values = 
            values
            |> Array.map(fun x -> int x)
            |> f
            |> Array.map(fun x -> byte x)
            |> Seq.toArray<byte>


        //Copy modified data and return bitmap
        let data = image.LockBits(rect, ImageLockMode.ReadWrite, image.PixelFormat)
        System.Runtime.InteropServices.Marshal.Copy(values,0,data.Scan0,values.Length)
        image.UnlockBits(data)

        image


    let loadDirFiles(dirName: String) = 
        let dir = new DirectoryInfo(dirName)
        let files = dir.GetFiles()
        files 

    let loadImg(dirName: String, fInfo : FileInfo) = 
       let img = new FileStream(dirName + @"/" + fInfo.Name, FileMode.Open) 
       let result = new Bitmap(img)
       result
    
    let getStride(width: int, format: PixelFormat) = 
        let bitsPerPixel = System.Drawing.Image.GetPixelFormatSize(format)
        let bytesPerPixel = (bitsPerPixel + 7) / 8
        let stride = 4 * ((width * bytesPerPixel + 3) /4)
        stride

    let toGrayScale(img: Bitmap) =
        let grayFrame : Image<Gray, byte> = new Image<Gray, byte>(img)
        let result = grayFrame.Bitmap
        result
    
    let binarise(value: int[]) =
        let result = value |> Array.map(fun x -> if x < 255/2 then 0 else 1)
        result

module main = 

    [<EntryPoint>]
    let main args =
        
        match args with 
        |   [|first|] -> 
                let dirName = first
                let files = ImgMan.loadDirFiles(dirName)
                let outputFile = new StreamWriter("result.txt")
                use engine = new TesseractEngine(@"./tessdata-master", "pol", EngineMode.Default)
                
                let result = 
                    files |> Array.map(fun x -> ImgMan.loadImg(dirName, x))
                          |> Array.map(fun x -> ImgMan.toGrayScale(x))
                          |> Array.map(fun x -> ImgMan.filterBitmap(x, ImgMan.binarise))
                          |> Array.map(fun x -> engine.Process(x)) 
                          |> Array.map(fun page -> page.GetText())
                result |> Array.map(fun page -> dbConnector.create({Text = page})) 
                1
        | _ -> failwith "Must have an argument"

       
