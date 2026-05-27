#include <vector>
#include <string>
#include <functional>
#include <cmath>
#include <algorithm>

struct TimeComplexity {
    std::string name;
    std::function<double(double)> transform;
};

extern "C" {
    __declspec(dllexport) const char* SingleValueTimeComplexity(double *testVal, int testValuesSize, double *timeVal, int timeValuesSize) {
        std::vector<double> singleValueTestCase(testVal, testVal + testValuesSize);
        std::vector<double> timeValues(timeVal, timeVal + timeValuesSize);
        size_t n = singleValueTestCase.size();

        if (n < 2) return "Inconclusive";
        double minT = *std::min_element(timeValues.begin(), timeValues.end());
        double maxT = *std::max_element(timeValues.begin(), timeValues.end());
        if ((maxT - minT) <= 3.0) {
            return u8"O(1)";
        }

        // Transform
        static std::vector<TimeComplexity> models = {
            {u8"O(logn)", [](double n) {return std::log2(n);}},
            {u8"O(\u221An)", [](double n) {return std::sqrt(n);}},
            {u8"O(n)", [](double n) {return n;}},
            {u8"O(nlogn)", [](double n) {return n * std::log2(n);}},
            {u8"O(n\u221An)", [](double n) {return n * sqrt(n);}},
            {u8"O(n\u00B2)", [](double n) {return n * n;}},
            {u8"O(n\u00B2\u221An)", [](double n) {return n * n * sqrt(n);}},
            {u8"O(n\u00B3)", [](double n) {return n * n * n;}},
        };
        TimeComplexity* best_model = nullptr;
        double BestRsq = -1000;

        for (TimeComplexity& model : models) {
            std::vector<double> xTransformed(n);
            double XM = 0, YM = 0;
            for (size_t i = 0; i < n; i++) {
                xTransformed[i] = model.transform(singleValueTestCase[i]);
                XM += xTransformed[i];
                YM += timeValues[i];
            }
            XM /= n, YM /= n;
            double intValUp = 0, intValDown = 0;
            double SStot = 0;
            for (size_t i = 0; i < n; i++) {
                intValUp += (xTransformed[i] - XM) * (timeValues[i] - YM);
                intValDown += (xTransformed[i] - XM) * (xTransformed[i] - XM);
                SStot += (timeValues[i] - YM) * (timeValues[i] - YM);
            }
            if (intValDown < 1e-9 || SStot < 1e-9) continue;

            double a = intValUp / intValDown;
            if (a <= 0) continue;
            double b = YM - a * XM;
            std::vector<double> timePrediction(n);
            for (size_t i = 0; i < n; i++)
                timePrediction[i] = a * xTransformed[i] + b;
            double SSres = 0;
            for (size_t i = 0; i < n; i++) {
                SSres += (timeValues[i] - timePrediction[i]) * (timeValues[i] - timePrediction[i]);
            }
            double Rsq = 1.0 - (SSres / SStot);
            if (Rsq > BestRsq)
                BestRsq = Rsq, best_model = &model;
        }
        return best_model ? best_model->name.c_str() : "Inconclusive";
    }
}
