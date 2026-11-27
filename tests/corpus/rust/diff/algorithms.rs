// Sorting, searching and a prime sieve.
use farm::*;

fn bubble_sort(v: &mut Vec<i32>) {
    let n = v.len();
    for i in 0..n {
        for j in 0..n - 1 - i {
            if v[j] > v[j + 1] {
                v.swap(j, j + 1);
            }
        }
    }
}

fn binary_search(v: &Vec<i32>, target: i32) -> i32 {
    let mut lo = 0;
    let mut hi = v.len() as i32 - 1;
    while lo <= hi {
        let mid = (lo + hi) / 2;
        if v[mid as usize] == target {
            return mid;
        } else if v[mid as usize] < target {
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }
    -1
}

fn primes(limit: usize) -> Vec<i32> {
    let mut is_prime = vec![true; limit + 1];
    let mut res = Vec::new();
    for i in 2..=limit {
        if is_prime[i] {
            res.push(i as i32);
            let mut j = i * i;
            while j <= limit {
                is_prime[j] = false;
                j += i;
            }
        }
    }
    res
}

fn main() {
    let mut v = vec![9, 4, 7, 1, 8, 2];
    bubble_sort(&mut v);
    println!("{:?}", v);
    println!("{} {}", binary_search(&v, 7), binary_search(&v, 5));
    println!("{:?}", primes(30));
}
