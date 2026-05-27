#include<fstream>
#include<iostream>
#include<cmath>
using namespace std;
ifstream fin("date.txt");
int main(){
    int n, sum = 0, x;
    cin >> n;
    x = n;
    for(int i = 0; i < x; i++)
       while(n > 0) {
        n /= 2;
       }
   //  for(int j = 0; j * j < n; j++)

    cout << sum;
    return 0;
}