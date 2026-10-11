using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.NodeDatas.Onnx.OpenCV.Base;
using G.VisionMaster.ResultPresenter;
using G.VisionMaster.ResultPresenter.ResultPresenters;
using OpenCvSharp.Dnn;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace G.NodeDatas.Onnx.OpenCV
{
    public static class OnnxExtension
    {
        public static Mat ToOutput(this Mat forward, int rowIndex = 0, int columnIndex = 1)
        {
            // 1 25200 16
            int rs = forward.Size(rowIndex);
            int cs = forward.Size(columnIndex);
            Mat result_mat_to_float = new Mat(rs, cs, MatType.CV_32F, forward.Data);
            float[] result_array = new float[forward.Cols * forward.Rows];
            // 将数据读取到数组中
            result_mat_to_float.GetArray(out result_array);
            return new Mat(rs, cs, MatType.CV_32F, result_array);
        }

        public static IEnumerable<Mat> GetMasks(this Mat forward, int count, int rowIndex = 0, int columnIndex = 1)
        {
            // 假设 tensorData 是获取到的 float[] 数组，长度为 1*2*192*192=73728
            //float[] tensorData = ... // 从模型输出获取的数据
            // 解析两个通道
            for (int c = 0; c < count; c++)
            {
                Mat mat = new Mat(rowIndex, columnIndex, MatType.CV_32FC1);
                // 复制数据到Mat
                for (int y = 0; y < 192; y++)
                {
                    for (int x = 0; x < 192; x++)
                    {
                        //  ToDo：待优化
                        // 计算一维数组中的索引
                        int index = c * 192 * 192 + y * 192 + x;
                        float cx = forward.At<float>(0, c, y, x);
                        mat.Set(y, x, cx);
                    }
                }
                yield return mat;
            }

            //// 如果需要合并为多通道Mat
            //Mat mergedMat = new Mat();
            //Cv2.Merge(channelMats, mergedMat); // 现在mergedMat是2通道的192x192图像
        }

        /// <summary>
        /// 根据结果数据提取
        /// </summary>
        /// <param name="outputData"></param>
        /// <param name="factor"></param>
        /// <returns></returns>
        public static IEnumerable<DefectBox> ToDefectBoxs(this Mat outputData, int confidenceIndex, float threshold, Func<float, float, float, float, Rect2f> convertToRect)
        {
            //float confidence0 = outputData.At<float>(0, 0);
            //float confidence1 = outputData.At<float>(1, 0);
            //System.Diagnostics.Debug.WriteLine(confidence0);
            if (outputData.Cols <= 4)
                yield break;
            for (int i = 0; i < outputData.Rows; i++)
            {
                float confidence_1 = outputData.At<float>(i, -1);
                float confidence0 = outputData.At<float>(i, 0);
                float confidence1 = outputData.At<float>(i, 1);
                float confidence2 = outputData.At<float>(i, 2);
                float confidence3 = outputData.At<float>(i, 3);
                float confidence4 = outputData.At<float>(i, 4);
                // 获取置信值
                float confidence = outputData.At<float>(i, 4);
                //float confidence = outputData.At<float>(i, -1);
                if (confidence < threshold)
                    continue;

                int propertyCount = outputData.Cols;
                double max_score, min_score;
                Point max_classId_point, min_classId_point;
                if (propertyCount > 5)
                {
                    Mat classes_scores = outputData.Row(i).ColRange(5, Math.Max(propertyCount, 5));//GetArray(i, 5, classes_scores);
                                                                                                   //Mat classes_scores = outputData.Row(i).ColRange(5, 85);//GetArray(i, 5, classes_scores);
                                                                                                   // 获取一组数据中最大值及其位置
                    Cv2.MinMaxLoc(classes_scores, out min_score, out max_score,
                        out min_classId_point, out max_classId_point);
                }
                else
                {
                    max_score = confidence;
                    max_classId_point = new Point();
                }
                // 置信度 0～1之间
                // 获取识别框信息
                if (max_score > threshold)
                {
                    float x = outputData.At<float>(i, 0);
                    float y = outputData.At<float>(i, 1);
                    float w = outputData.At<float>(i, 2);
                    float h = outputData.At<float>(i, 3);
                    Rect2f box = convertToRect(x, y, w, h);
                    yield return new DefectBox() { Score = confidence, Box = box, ClassId = max_classId_point.X };
                    //yield break;
                    ////cx *= 640;
                    ////cy *= 640;
                    ////ow *= 640;
                    ////oh *= 640;
                    //int x = (int)((cx - 0.5 * ow) * factor);
                    //int y = (int)((cy - 0.5 * oh) * factor);
                    ////模型输出的 BOX 通常有 两种格式：
                    ////[x_center, y_center, width, height]
                    ////中心点坐标 + 宽高（YOLO 系列常用）。
                    ////[x1, y1, x2, y2]
                    ////左上角和右下角坐标（Faster R-CNN 等常用）。
                    ////int x = (int)(cx * factor);
                    ////int y = (int)(cy * factor);
                    //int width = (int)(ow * factor);
                    //int height = (int)(oh * factor);
                    //Rect box = new Rect();
                    //box.X = x;
                    //box.Y = y;
                    //box.Width = width;
                    //box.Height = height;
                    //yield return new DefectBox() { Score = (float)max_score, Box = box, ClassId = max_classId_point.X };

                }
            }
        }

        public static IEnumerable<DefectBox> ToNMSBoxes(this IEnumerable<DefectBox> defectBoxes, float threshold = 0.25f, float nmsThreshold = 0.45f)
        {
            // NMS非极大值抑制
            int[] indexes = new int[defectBoxes.Count()];
            List<Rect2f> boxes = defectBoxes.Select(x => x.Box).ToList();
            List<float> confidences = defectBoxes.Select(x => x.Score).ToList();
            List<int> classIds = defectBoxes.Select(x => x.ClassId).ToList();
            CvDnn.NMSBoxes(boxes.Select(x => x.ToCVRect()), confidences, threshold, nmsThreshold, out indexes);
            for (int i = 0; i < indexes.Length; i++)
            {
                int index = indexes[i];
                int idx = classIds[index];
                Rect2f box = boxes[index];
                yield return new DefectBox() { Score = (float)confidences[index], Box = box, ClassId = idx };
            }

            //string label = string.Format("{0} {1:0.0}%", class_names[idx], confidences[index] * 100);
            //Size textSize = Cv2.GetTextSize(label, HersheyFonts.HersheyTriplex, 0.5, 1, out int baseline);
            //Cv2.Rectangle(result_image, box, outputLineColor.ToScalar(), thickness, LineTypes.Link8);
            //Cv2.Rectangle(result_image, new Point(box.TopLeft.X, box.TopLeft.Y - 20),
            //    new Point(box.BottomRight.X, box.TopLeft.Y), outputLineColor.ToScalar(), -1);
            //Cv2.PutText(result_image, label,
            //    new Point(box.X, box.Y - baseline),
            //    HersheyFonts.HersheyTriplex, 0.5, outputLabelColor.ToScalar(), 1);
        }

        public static void DrawDetectBoxes(this Mat image, Color boxColor, int boxThickness = 1, params DefectBox[] defectBoxes)
        {
            foreach (DefectBox defectBox in defectBoxes)
            {
                Rect box = defectBox.Box.ToCVRect();
                Cv2.Rectangle(image, box, boxColor.ToScalar(), boxThickness, LineTypes.Link8);
            }
        }

        public static IEnumerable<Tuple<DefectBox, string, double>> DrawDetectBoxLabels(this Mat image, IEnumerable<DefectBox> defectBoxes, Color labelBackColor, Color labelColor, List<string> classNames, bool useScore = true)
        {
            foreach (DefectBox nmsbox in defectBoxes)
            {
                Rect box = nmsbox.Box.ToCVRect();
                string name = classNames.Count > nmsbox.ClassId ? classNames[nmsbox.ClassId] : classNames.Count == 1 ? classNames[0] : string.Empty;
                double score = nmsbox.Score;
                string label = useScore ? string.Format("{0} {1:0.0}%", name, score * 100) : name;
                if (string.IsNullOrEmpty(label))
                    continue;
                Size textSize = Cv2.GetTextSize(label, HersheyFonts.HersheyTriplex, 0.5, 1, out int baseline);
                Cv2.Rectangle(image, new Point(box.TopLeft.X, box.TopLeft.Y - 20),
                    new Point(box.BottomRight.X, box.TopLeft.Y), labelBackColor.ToScalar(), -1);
                if (string.IsNullOrEmpty(name) && useScore == false)
                    continue;
                Cv2.PutText(image, label,
                    new Point(box.X, box.Y - baseline),
                    HersheyFonts.HersheyTriplex, 0.5, labelColor.ToScalar(), 1);
                yield return Tuple.Create(nmsbox, name, score);
            }
        }

        public static IResultPresenter ToResultPresenter(this IEnumerable<Tuple<DefectBox, string, double>> tuples)
        {
            return tuples.Select(x => new ScoreRectangleResultItem(x.Item1.Box.ToCVRect().ToWindowRect(), Math.Round(x.Item3, 2)) { Name = x.Item2 }).ToDataGridResultPresenter();

            //return tuples.ToRectangleDataGridResultPresenter(x => x.Item1.Box.ToCVRect().ToWindowRect(), x => x.Item2);
        }

        public static IEnumerable<Mat> CreateForwards(this Net net)
        {
            // 获取所有输出层
            string[] outputNames = net.GetUnconnectedOutLayersNames();
            List<Mat> outputs = outputNames.Select(name => net.Forward(name)).ToList();
            foreach (Mat forward in outputs)
            {
                System.Diagnostics.Debug.WriteLine($"{forward.Size(0)} {forward.Size(1)} {forward.Size(2)}  {forward.Size(3)}   {forward.Size(4)}   {forward.Size(5)}");
                yield return forward;
            }
            //// 模型推理，读取推理结果
            //Mat forward = net.Forward();
            //yield return forward;
        }

        public static IEnumerable<Mat> GetForwards(this Mat image, string model_path, Size inputSize, Scalar mean, Scalar std, float blobScaleFactor = 255.0f)
        {
            using Net net = CvDnn.ReadNetFromOnnx(model_path);
            Mat blob_image = image.ToInputMat(blobScaleFactor, inputSize, mean, std);
            net.SetInput(blob_image);
            List<Mat> forwards = net.CreateForwards().ToList();
            return forwards;

        }

        public static IEnumerable<DefectBox> DefectBoxes(this Mat image, string model_path, Size inputSize, Scalar mean, Scalar std, int rowIndex = 1, int columnIndex = 2, int confidenceIndex = 4, float threshold = 0.25f, float nmsThreshold = 0.45f, float blobScaleFactor = 255.0f, BoxCoordinateMode boxCoordinateMode = BoxCoordinateMode.AbsolutePixels, BoxGeometryType boxGeometryType = BoxGeometryType.CenterWithSize)
        {
            int maxLen = image.Cols > image.Rows ? image.Cols : image.Rows;
            float factor = maxLen / (float)inputSize.Width;

            // 模型推理，读取推理结果
            List<Mat> forwards = image.GetForwards(model_path, inputSize, mean, std, blobScaleFactor).ToList();
            foreach (Mat forward in forwards)
            {
                //  **目标检测**     | [1, 100, 5+4]               | 100个框的类别概率+坐标
                Mat output = forward.ToOutput(rowIndex, columnIndex);
                List<DefectBox> defectBoxes = output.ToDefectBoxs(confidenceIndex, threshold, (x, y, w, h) =>
                {
                    float v1 = x * factor;
                    float v2 = y * factor;
                    float v3 = w * factor;
                    float v4 = h * factor;
                    if (boxCoordinateMode == BoxCoordinateMode.NormalizedRatio)
                    {
                        v1 *= inputSize.Width;
                        v2 *= inputSize.Height;
                        v3 *= inputSize.Width;
                        v4 *= inputSize.Height;
                    }
                    if (boxGeometryType == BoxGeometryType.CornerPoints)
                    {
                        return new Rect2f(v1, v2, (v3 - v1), (v4 - v2));
                    }
                    if (boxGeometryType == BoxGeometryType.PointWithSize)
                    {
                        return new Rect2f(v1, v2, v3, v4);
                    }
                    if (boxGeometryType == BoxGeometryType.CenterWithSize)
                    {
                        float width = v3;
                        float height = v4;
                        float left = v1 - 0.5f * width;
                        float top = v2 - 0.5f * height;
                        return new Rect2f(left, top, width, height);
                    }

                    if (boxGeometryType == BoxGeometryType.PolarWithAngle)
                    {
                        //// 极坐标的 radius 转换为矩形的 width/height
                        //// 如果是圆，width=height=2*radius；如果是椭圆需单独处理
                        //float width = 2 * radius;
                        //float height = 2 * radius;

                        //return new RotatedRect(
                        //    center: new Point2f(cx, cy),
                        //    size: new Size2f(width, height),
                        //    angle: angle
                        //);
                        // 计算圆的包围盒（忽略角度）
                        float x_min = v1 - v3;
                        float y_min = v2 - v3;
                        float x_max = v1 + v3;
                        float y_max = v2 + v3;
                        return new Rect2f(x_min, y_min, x_max - x_min, y_max - y_min);
                    }

                    throw new NotImplementedException();
                }).ToList();
                IEnumerable<DefectBox> nmsBoxes = defectBoxes.ToNMSBoxes(threshold, nmsThreshold).ToList();
                foreach (DefectBox nmsBox in nmsBoxes)
                {
                    yield return nmsBox;
                }
            }
        }

        public static IEnumerable<float> InferValues(this Mat image, string model_path, Size inputSize, Scalar mean, Scalar std, int rowIndex = 1, int columnIndex = 2, float blobScaleFactor = 255.0f)
        {
            // 模型推理，读取推理结果
            List<Mat> forwards = image.GetForwards(model_path, inputSize, mean, std, blobScaleFactor).ToList();
            foreach (Mat forward in forwards)
            {
                //| **图像分类**     | [1, 1000]                   | 1000类的概率分布
                //yield return forward.At<float>(0, 0);
                Mat output = forward.ToOutput(rowIndex, columnIndex);
                int valueCount = forward.Size(columnIndex);
                for (int i = 0; i < valueCount; i++)
                {
                    yield return output.At<float>(0, i);
                }

                System.Diagnostics.Debug.WriteLine(forward.At<float>(0, 1));
                System.Diagnostics.Debug.WriteLine(forward.At<float>(0, 2));
                System.Diagnostics.Debug.WriteLine(forward.At<float>(0, 3));
                System.Diagnostics.Debug.WriteLine(forward.At<float>(0, 4));

                System.Diagnostics.Debug.WriteLine(output.At<float>(0, 0));
                System.Diagnostics.Debug.WriteLine(output.At<float>(0, 1));
                System.Diagnostics.Debug.WriteLine(output.At<float>(0, 2));
                System.Diagnostics.Debug.WriteLine(output.At<float>(0, 3));
                System.Diagnostics.Debug.WriteLine(output.At<float>(0, 4));
            }
        }

        public static IEnumerable<Tuple<string, float>> Classification(this Mat image, string model_path, Size inputSize, Scalar mean, Scalar std, List<string> classNames, int rowIndex = 1, int columnIndex = 2, float blobScaleFactor = 255.0f)
        {
            IEnumerable<Tuple<string, float>> GetTuples(List<float> values)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    string className = classNames != null && classNames.Count > i ? classNames[i] : string.Empty;
                    yield return Tuple.Create(className, values[i]);
                }

            }

            IEnumerable<float> values = image.InferValues(model_path, inputSize, mean, std, rowIndex, columnIndex, blobScaleFactor);
            return GetTuples(values.ToList());
        }

        public static IEnumerable<Tuple<float, Mat>> SemanticSegmentation(this Mat image, string model_path, Size inputSize, Scalar mean, Scalar std, int rowIndex = 1, int columnIndex = 2, float blobScaleFactor = 255.0f)
        {
            // 模型推理，读取推理结果
            List<Mat> forwards = image.GetForwards(model_path, inputSize, mean, std, blobScaleFactor).ToList();
            foreach (Mat forward in forwards)
            {
                //| **图像分类**     | [1, 1000]                   | 1000类的概率分布
                //yield return forward.At<float>(0, 0);
                int classCount = forward.Size(1);
                //for (int i = 0; i < classCount; i++)
                //{
                //    var type = forward.At<float>(0, i);
                //    //Mat output = forward.ToOutput(inputSize.Width, inputSize.Height);
                //    yield return Tuple.Create(type, output);
                //}
                IEnumerable<Mat> outputs = forward.GetMasks(classCount, inputSize.Width, inputSize.Height);
                foreach (Mat output in outputs)
                {
                    yield return Tuple.Create(1.0f, output);
                }
            }
        }

        public static IEnumerable<string> GetClassNames(this string lablePath)
        {
            if (!File.Exists(lablePath))
            {
                if (!string.IsNullOrEmpty(lablePath))
                {
                    foreach (string item in lablePath.Split(", ,\"".ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
                    {
                        yield return item;
                    }
                }
                yield break;
            }
            using StreamReader sr = new StreamReader(lablePath);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                yield return line;
            }
        }

        /// <summary>
        /// 将输出掩码转换为原图的掩码
        /// </summary>
        /// <param name="forward"></param>
        /// <param name="src"></param>
        /// <returns></returns>
        public static Mat ToSrcMask(this Mat forward, int rows, int cols)
        {
            double scale = 1.0 * cols / rows;
            Rect roi = cols > rows ? new Rect(0, 0, forward.Cols, (int)(forward.Rows / scale))
                : new Rect(0, 0, (int)(forward.Cols * scale), (int)(forward.Rows));
            //反归一化
            Mat mask8u1 = new Mat();
            forward.ConvertTo(mask8u1, MatType.CV_8UC1, 255.0);
            //截取Roi
            Mat cropped = mask8u1.SubMat(roi);
            //调整回原值尺寸
            Mat resize = new Mat();
            Cv2.Resize(cropped, resize, new Size(cols, rows));
            // 如果掩码是二值图(0和255)，确保值正确
            // 如果不是，可以进行阈值处理
            Mat binary = new Mat();
            Cv2.Threshold(resize, binary, 128, 255, ThresholdTypes.Binary);
            return binary;
        }

        /// <summary>
        /// 将原图归一化处理到模型输入格式
        /// </summary>
        /// <param name="image"></param>
        /// <param name="blobScaleFactor"></param>
        /// <param name="inputSize"></param>
        /// <param name="mean"></param>
        /// <param name="std"></param>
        /// <returns></returns>
        public static Mat ToInputMat(this Mat image, float blobScaleFactor, Size inputSize, Scalar mean, Scalar std)
        {
            int maxLen = image.Cols > image.Rows ? image.Cols : image.Rows;
            // 将图片放在矩形背景下
            Mat max_image = Mat.Zeros(new Size(maxLen, maxLen), MatType.CV_8UC3);
            Rect roi = new Rect(0, 0, image.Cols, image.Rows);
            // 将图片转换成Onnx下的RGB格式BlobFromImage中swapRB=true应该不用转换颜色
            //var rgbImage = image.CvtColor(ColorConversionCodes.BGR2RGB);
            image.CopyTo(new Mat(max_image, roi));
            // 数据归一化
            Mat blob_image = CvDnn.BlobFromImage(max_image, 1 / blobScaleFactor, inputSize, mean, true, false);

            if (!std.IsReal())
            {
                // 手动进行标准差归一化
                //// OpenCV 的 Mat 是多维数组，使用 Divide 方法对每个通道除以标准差
                //Mat stdMat = new Mat(blob_image.Size(), blob_image.Type(), std); // 创建与 blob 相同大小的 Mat，值为标准差

                // 2. 创建一个 1x3 Mat 存储标准差（每个通道一个值）
                //Mat stdMat = new Mat(1, 3, blob_image.Type(), std);
                //Mat stdMat = new Mat(1, 3, MatType.CV_32FC1, std);
                // 生成一个4D的stdMat [1,3,1,1]，与blob_image形状兼容
                Mat stdMat = new Mat(new[] { blob_image.Size(0), blob_image.Size(1), blob_image.Size(2), blob_image.Size(3) }, MatType.CV_32F, std);
                Cv2.Divide(blob_image, stdMat, blob_image);

                //Cv2.Divide(blob_image, stdMat, blob_image); // 对 blob 的每个通道除以标准差
            }
            return blob_image;
        }

    }

    // ONNX（Open Neural Network Exchange）格式的图像处理模型根据任务类型可分为几大类，每类的输出格式各有特点。以下是主要分类及对应的输出结构说明：

    //---

    //### **1. 图像分类（Image Classification）**
    //- ** 任务**：识别图像中的主要物体类别（单标签或多标签）。
    //- ** 典型模型**：ResNet、MobileNet、EfficientNet等。
    //- ** 输出格式**：
    //  - ** 单分类**：`[batch_size, num_classes]`的二维张量，每行是每个类别的概率（Softmax后，总和为1）。
    //    ```python
    //# 示例输出（batch_size=1, num_classes=1000）：
    //    [[0.01, 0.8, 0.05, ..., 0.002]]  # 最高概率对应类别为预测结果
    //    ```
    //  - ** 多标签分类**：`[batch_size, num_classes]`的二维张量，每个值通过Sigmoid独立激活（0~1之间），表示该类别的存在概率。

    //---

    //### **2. 目标检测（Object Detection）**
    //- ** 任务**：检测图像中多个物体的类别和位置（边界框）。
    //- ** 典型模型**：YOLO、SSD、Faster R-CNN等。
    //- ** 输出格式**：
    //  - ** 两种常见形式**：
    //    1. ** 统一张量**：`[batch_size, num_boxes, (class_probs + bbox_coords)]`  
    //       每行包含类别概率（如`[prob_class1, prob_class2, ...]`）和边界框坐标（如`[x_min, y_min, x_max, y_max]`或`[cx, cy, w, h]`）。
    //       ```python
    //# 示例（batch_size=1, num_boxes=100, 5类+4坐标）：
    //       [[
    //           [0.9, 0.1, 0.05, 0.2, 0.1, 0.5, 0.5, 0.3, 0.3],  # 第一检测框
    //           [0.1, 0.8, 0.05, 0.1, 0.1, 0.1, 0.1, 0.2, 0.2],  # 第二检测框
    //           ...
    //       ]]
    //       ```
    //    2. ** 多输出头**：某些模型（如YOLOv8）可能输出多个张量（如分类得分、框坐标、锚点信息等）。

    //---

    //### **3. 语义分割（Semantic Segmentation）**
    //- ** 任务**：对每个像素进行分类，输出类别掩码。
    //- ** 典型模型**：UNet、DeepLab等。
    //- ** 输出格式**：`[batch_size, num_classes, height, width]`的四维张量，表示每个像素点对各类别的概率。
    //  ```python
    //# 示例（batch_size=1, num_classes=21, 512x512图像）：
    //  output.shape  # (1, 21, 512, 512)
    //  ```
    //  - 后处理时需对每个像素取`argmax`得到最终类别标签图。

    //---

    //### **4. 实例分割（Instance Segmentation）**
    //- **任务**：区分不同物体的实例，同时生成掩码。
    //- **典型模型**：Mask R-CNN。
    //- **输出格式**：通常为** 多个输出**：
    //  - **检测结果**：类似目标检测（类别+边界框）。
    //  - **掩码结果**：`[batch_size, num_masks, height, width]`，每个掩码对应一个检测到的实例。

    //---

    //### **5. 关键点检测（Keypoint Detection）**
    //- **任务**：检测物体关键点（如人脸特征点、人体姿态）。
    //- **典型模型**：HRNet、OpenPose。
    //- **输出格式**：
    //  - **热图形式**：`[batch_size, num_keypoints, heatmap_height, heatmap_width]`，通过热图峰值确定关键点位置。
    //  - **坐标形式**：直接输出关键点坐标`[batch_size, num_keypoints, 2]`（x,y）。

    //---

    //### **6. 超分辨率/图像生成（Super-Resolution/GANs）**
    //- **任务**：生成高分辨率或风格化图像。
    //- **典型模型**：ESRGAN、StyleGAN。
    //- **输出格式**：`[batch_size, channels, height, width]`，与输入图像尺寸相同或放大后的RGB图像（值范围可能为[0,1]或[-1,1]）。

    //---

    //### **输出格式总结表**
    //| 任务类型         | 输出形状示例                  | 输出内容说明                     |
    //|------------------|-----------------------------|--------------------------------|
    //| **图像分类**     | [1, 1000]                   | 1000类的概率分布                |
    //| **目标检测**     | [1, 100, 5+4]               | 100个框的类别概率+坐标          |
    //| **语义分割**     | [1, 21, 512, 512]           | 每像素21类的概率                |
    //| **实例分割**     | 检测框+[1, 10, 256, 256]    | 10个实例的掩码                  |
    //| **关键点检测**   | [1, 17, 64, 64]             | 17个关键点的热图                |
    //| **超分辨率**     | [1, 3, 1024, 1024]          | 生成的RGB图像                   |

    //---

    //### **注意事项**
    //1. **动态形状**：某些模型支持动态batch或尺寸（如`[None, 3, None, None]`）。
    //2. **后处理**：目标检测/分割的输出通常需要NMS（非极大值抑制）等后处理。
    //3. **模型差异**：不同框架导出的ONNX模型可能有细微差别（如坐标归一化方式）。

    //建议使用Netron可视化ONNX模型结构，明确输入输出张量的具体含义。
}
