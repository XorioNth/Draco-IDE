#include<fstream>
#include<iostream>
#include<cmath>
using namespace std;
ifstream fin("date.txt");
int main(){
    int n, sum = 0;
    cin >> n;
    for(int i = 0; i < n; i++)
       sum++;
    cout << sum;
    return 0;
}