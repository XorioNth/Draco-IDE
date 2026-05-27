#include <iostream>
#include <algorithm>
#include <vector>
using namespace std;
vector<int> arr;
int main() {
    int n, sum = 0;
    cin >> n;
    for(int i = 0; i < n; i++) {
       arr.push_back((7 * i) + 15);
       sum++;
    }
    sort(arr.begin(), arr.end());
    return 0;
}