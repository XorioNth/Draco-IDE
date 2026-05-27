#include<fstream>
#include<iostream>
#include<cmath>
using namespace std;
ifstream fin("date.txt");
int main(){
    int n, sum = 0;
    cin >> n;
    for(int i = 0; i < n; i++)
     for(int j = 0; j *j < n; j++)
       sum++;
    cout << sum;
    return 0;
}